#pragma once
#include <external/nlohmann/json.hpp>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <limits>
#include <map>
#include <string>
#include <vector>
#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

// Data-only UI persistence. No game memory, input hooks or Lua execution.
namespace mcb_ui_v62 {
using json = nlohmann::json;
namespace fs = std::filesystem;
inline constexpr std::uintmax_t max_file_bytes = 1024 * 1024;
struct field { std::string kind; int low{}, high{}, count{}; };
using schema = std::map<std::string, field>;

inline bool normalize(const json& in, const schema& fields, json& out, std::string& error) {
    if (!in.is_object() || in.size() > 4096) { error = "Invalid panel state object"; return false; }
    json result = json::object();
    for (auto it = in.begin(); it != in.end(); ++it) {
        if (it.key().size() > 512) { error = "State key is too long"; return false; }
        auto found = fields.find(it.key());
        if (found == fields.end()) continue;
        const auto& f = found->second; const auto& v = it.value();
        auto invalid = [&] { error = "Invalid value: " + it.key(); return false; };
        if (f.kind == "toggle") { if (!v.is_boolean()) return invalid(); result[it.key()] = v; }
        else if (f.kind == "tags") {
            if (!v.is_array() || v.size() > 32 || static_cast<int>(v.size()) != f.count) return invalid();
            for (const auto& e : v) if (!e.is_boolean()) return invalid();
            result[it.key()] = v;
        } else if (f.kind == "color") {
            if (!v.is_number_integer()) return invalid();
            const double n = v.get<double>();
            if (n < 0 || n > 4294967295.0) return invalid();
            result[it.key()] = static_cast<std::uint32_t>(n);
        } else {
            if (!v.is_number_integer()) return invalid();
            const double n = v.get<double>();
            if (!std::isfinite(n)) return invalid();
            result[it.key()] = static_cast<int>(std::clamp(n, double(f.low), double(f.high)));
        }
    }
    out = std::move(result); error.clear(); return true;
}
inline bool read_json(const fs::path& p, json& out, std::string& error) {
    try {
        std::error_code ec; const auto size = fs::file_size(p, ec);
        if (ec) { error = "File not found or unreadable"; return false; }
        if (size > max_file_bytes) { error = "JSON file exceeds 1 MiB"; return false; }
        std::ifstream file(p, std::ios::binary);
        if (!file) { error = "Cannot open JSON file"; return false; }
        std::string data(static_cast<std::size_t>(size), '\0');
        if (size && !file.read(data.data(), static_cast<std::streamsize>(size))) { error = "Incomplete JSON read"; return false; }
        if (file.peek() != std::char_traits<char>::eof()) { error = "JSON changed while reading"; return false; }
        int depth=0; bool quoted=false, escape=false;
        for(char c:data) {
            if(quoted) { if(escape)escape=false; else if(c=='\\')escape=true; else if(c=='"')quoted=false; }
            else if(c=='"')quoted=true;
            else if(c=='{' || c=='[') { if(++depth>32) {error="JSON nesting exceeds 32 levels";return false;} }
            else if(c=='}' || c==']')--depth;
        }
        json value = json::parse(data, nullptr, false);
        if (value.is_discarded() || !value.is_object()) { error = "Malformed JSON object"; return false; }
        out = std::move(value); error.clear(); return true;
    } catch (const std::exception& e) { error = std::string("JSON read failed: ") + e.what(); return false; }
}
inline bool write_json(const fs::path& p, const json& value, std::string& error) {
    fs::path temp;
    try {
        const auto text = value.dump(2);
        if (text.size() > max_file_bytes) { error = "JSON output exceeds 1 MiB"; return false; }
        std::error_code ec; fs::create_directories(p.parent_path(), ec);
        if (ec) { error = "Cannot create settings directory"; return false; }
        static std::atomic<unsigned long long> serial{0};
        temp = p;
#ifdef _WIN32
        temp += ".tmp." + std::to_string(GetCurrentProcessId()) + "." + std::to_string(++serial);
#else
        temp += ".tmp." + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + "." + std::to_string(++serial);
#endif
        { std::ofstream f(temp, std::ios::binary | std::ios::trunc);
          if (!f) { error = "Cannot create temporary settings file"; return false; }
          f.write(text.data(), static_cast<std::streamsize>(text.size())); f.flush();
          if (!f) { f.close(); fs::remove(temp, ec); error = "Settings write failed"; return false; }
          f.close(); if (!f) { fs::remove(temp, ec); error = "Settings close failed"; return false; } }
#ifdef _WIN32
        if (!MoveFileExW(temp.c_str(), p.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            error = "Cannot replace settings file (Windows " + std::to_string(GetLastError()) + ")";
            fs::remove(temp, ec); return false;
        }
#else
        fs::rename(temp, p, ec); if (ec) { error = "Cannot replace settings file"; fs::remove(temp, ec); return false; }
#endif
        error.clear(); return true;
    } catch (const std::exception& e) { std::error_code ec; if (!temp.empty()) fs::remove(temp, ec); error = std::string("Save failed: ") + e.what(); return false; }
}
inline json snapshot(std::string_view name, const json& state) {
    return json{{"format", "MCB UI-only config snapshot"}, {"schema_version", 2},
                {"config", std::string(name)}, {"gameplay_execution", false}, {"panel_state", state}};
}
inline bool decode_snapshot(const json& j, const schema& fields, json& state, std::string& name, std::string& error) {
    if (!j.is_object() || !j.contains("format") || !j["format"].is_string() ||
        j["format"] != "MCB UI-only config snapshot" || !j.contains("gameplay_execution") ||
        !j["gameplay_execution"].is_boolean() || j["gameplay_execution"].get<bool>() ||
        !j.contains("panel_state") || !j.contains("config") || !j["config"].is_string()) {
        error = "Not an MCB local UI snapshot"; return false;
    }
    name = j["config"].get<std::string>();
    if (name.empty() || name.size() > 96) { error = "Config name must contain 1-96 UTF-8 bytes"; return false; }
    if (!normalize(j["panel_state"], fields, state, error)) return false;
    return true;
}

class config_store {
    fs::path root_; schema fields_; json data_;
    bool writable_{true}; std::string startup_error_;
    static json defaults() {
        json rows = json::array();
        for (auto name : {"Global", "Default", "Legit", "Visuals"})
            rows.push_back({{"id", std::string("builtin.") + name}, {"name", name}, {"state", json::object()}});
        return json{{"version", 2}, {"next_id", 1}, {"active", "builtin.Global"}, {"configs", std::move(rows)}};
    }
    bool validate(json& j, std::string& error) const {
        if (!j.is_object() || !j.contains("version") || j["version"] != 2 || !j.contains("configs") ||
            !j["configs"].is_array() || j["configs"].size() < 4 || j["configs"].size() > 128 ||
            !j.contains("next_id") || !j["next_id"].is_number_integer() ||
            j["next_id"].get<double>() < 1 || j["next_id"].get<double>() > 1000000000 ||
            !j.contains("active") || !j["active"].is_string()) { error = "Invalid config database"; return false; }
        std::vector<std::string> ids; const auto base = defaults();
        for (std::size_t i = 0; i < j["configs"].size(); ++i) {
            auto& row = j["configs"][i];
            if (!row.is_object() || !row.contains("id") || !row["id"].is_string() || !row.contains("name") ||
                !row["name"].is_string() || !row.contains("state")) { error = "Invalid config record"; return false; }
            const auto id = row["id"].get<std::string>(), name = row["name"].get<std::string>();
            if (id.empty() || id.size() > 64 || name.empty() || name.size() > 96 || std::find(ids.begin(), ids.end(), id) != ids.end()) {
                error = "Invalid or duplicate config identity"; return false;
            }
            if (i < 4 && (row["id"] != base["configs"][i]["id"] || row["name"] != base["configs"][i]["name"])) {
                error = "Built-in config identities are protected"; return false;
            }
            json normalized; if (!normalize(row["state"], fields_, normalized, error)) return false;
            row["state"] = std::move(normalized); ids.push_back(id);
        }
        if (std::find(ids.begin(), ids.end(), j["active"].get<std::string>()) == ids.end()) { error = "Unknown active config"; return false; }
        return true;
    }
    bool commit(json candidate, std::string& error) {
        if (!writable_) { error = startup_error_ + "; original file preserved"; return false; }
        if (!validate(candidate, error) || !write_json(root_ / "panel_configs_v62.json", candidate, error)) return false;
        data_ = std::move(candidate); return true;
    }
public:
    config_store(fs::path root, schema fields) : root_(std::move(root)), fields_(std::move(fields)), data_(defaults()) { reload(startup_error_); }
    const json& records() const { return data_["configs"]; }
    const fs::path& directory() const { return root_; }
    const schema& fields() const { return fields_; }
    const std::string& startup_error() const { return startup_error_; }
    bool writable() const { return writable_; }
    std::size_t size() const { return records().size(); }
    int index(std::string_view id) const { for (int i=0; i<int(size()); ++i) if (records()[i]["id"] == id) return i; return -1; }
    int active_index() const { return std::max(0, index(data_["active"].get<std::string>())); }
    std::string name(int i) const { return records().at(i)["name"].get<std::string>(); }
    bool reload(std::string& error) {
        const auto path = root_ / "panel_configs_v62.json"; std::error_code ec;
        if (!fs::exists(path, ec) && !ec) { error.clear(); return true; }
        json candidate;
        if (!read_json(path, candidate, error) || !validate(candidate, error)) {
            writable_ = false; startup_error_ = error; return false;
        }
        data_ = std::move(candidate); writable_ = true; startup_error_.clear(); error.clear(); return true;
    }
    bool save(int i, const json& state, std::string& error) {
        if (i < 0 || i >= int(size())) { error = "No config selected"; return false; }
        json normalized; if (!normalize(state, fields_, normalized, error)) return false;
        auto candidate = data_; candidate["configs"][i]["state"] = std::move(normalized);
        candidate["active"] = candidate["configs"][i]["id"]; return commit(std::move(candidate), error);
    }
    bool load(int i, json& state, std::string& error) {
        if (i < 0 || i >= int(size())) { error = "No config selected"; return false; }
        auto candidate = data_; candidate["active"] = candidate["configs"][i]["id"];
        if (!commit(std::move(candidate), error)) return false;
        state = records()[i]["state"]; return true;
    }
    bool add(std::string name, const json& state, int& selected, std::string& error) {
        if (size() >= 128) { error = "Maximum 128 configs"; return false; }
        if (name.empty() || name.size() > 96) { error = "Invalid config name"; return false; }
        json normalized; if (!normalize(state, fields_, normalized, error)) return false;
        auto candidate = data_; int next = candidate["next_id"].get<int>();
        if (next >= 1000000000) { error = "Config identity limit reached"; return false; }
        std::string id;
        do { id = "custom." + std::to_string(next++); } while (index(id) >= 0);
        candidate["next_id"] = next;
        candidate["configs"].push_back({{"id", id}, {"name", name}, {"state", std::move(normalized)}});
        if (!commit(std::move(candidate), error)) return false;
        selected = int(size()) - 1; return true;
    }
    bool erase(int i, std::string& error) {
        if (i < 4 || i >= int(size())) { error = "Built-in configs cannot be deleted"; return false; }
        auto candidate = data_;
        if (candidate["active"] == candidate["configs"][i]["id"]) candidate["active"] = "builtin.Global";
        candidate["configs"].erase(candidate["configs"].begin()+i); return commit(std::move(candidate), error);
    }
    bool export_to(const fs::path& path, int i, const json& working, std::string& error) const {
        if (i < 0 || i >= int(size())) { error = "No config selected"; return false; }
        json normalized; if (!normalize(working, fields_, normalized, error)) return false;
        return write_json(path, snapshot(name(i), normalized), error);
    }
    bool import_from(const fs::path& path, int& selected, std::string& error) {
        json j, state; std::string name;
        if (!read_json(path, j, error) || !decode_snapshot(j, fields_, state, name, error)) return false;
        return add(name, state, selected, error);
    }
    bool restore(const fs::path& path, json& state, std::string& error) const {
        json j; std::string name; return read_json(path, j, error) && decode_snapshot(j, fields_, state, name, error);
    }
    // One-time read-only migration; the v6.1 source files remain untouched.
    bool migrate_v61(std::string& error) {
        std::error_code ec;
        if (fs::exists(root_ / "panel_configs_v62.json", ec) || !fs::exists(root_ / "panel_configs.json", ec)) return true;
        json old; if (!read_json(root_ / "panel_configs.json", old, error)) return false;
        if (!old.contains("names") || !old["names"].is_array() || old["names"].size() < 4 || old["names"].size() > 128) { error = "Legacy config index is invalid"; return false; }
        auto candidate = defaults();
        for (int i=0; i<int(old["names"].size()); ++i) {
            if (!old["names"][i].is_string()) { error = "Legacy config name is invalid"; return false; }
            std::string name = old["names"][i].get<std::string>();
            if (name.empty() || name.size() > 96) { error = "Legacy config name is too long"; return false; }
            json state = json::object(); const auto path = root_ / ("config_"+std::to_string(i)+".json");
            if (fs::exists(path, ec)) { json j; std::string ignored; if (!read_json(path,j,error) || !decode_snapshot(j,fields_,state,ignored,error)) return false; }
            if (i < 4) candidate["configs"][i]["state"] = state;
            else candidate["configs"].push_back({{"id", "legacy."+std::to_string(i)}, {"name", name}, {"state", state}});
        }
        return commit(std::move(candidate), error);
    }
};
inline json default_drafts() {
    json files = json::array();
    for (int i=0;i<4;++i) files.push_back(json::array({
        json::array({"-- MCB local draft; execution disabled", "function on_frame(dt)", "    -- edit this preview locally", "end"}),
        json::array({"-- Independent local settings draft", "return {", "    opacity = 0.96", "}"})
    }));
    return json{{"version",2},{"buffers",files}};
}
inline bool validate_drafts(const json& j, std::string& error) {
    if (!j.is_object() || !j.contains("version") || j["version"] != 2 || !j.contains("buffers") || !j["buffers"].is_array() || j["buffers"].size()!=4) {
        error="Invalid editor draft database"; return false;
    }
    for (const auto& script:j["buffers"]) {
        if (!script.is_array() || script.size()!=2) { error="Each script requires two draft tabs"; return false; }
        for (const auto& file:script) {
            if (!file.is_array() || file.empty() || file.size()>64) { error="Editor supports 1-64 lines per tab"; return false; }
            for (const auto& line:file) if (!line.is_string() || line.get_ref<const std::string&>().size()>512) { error="Invalid editor line"; return false; }
        }
    }
    error.clear(); return true;
}
} // namespace mcb_ui_v62
