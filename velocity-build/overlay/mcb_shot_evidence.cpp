#include <pch/pch.hpp>

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>

#include "mcb_shot_evidence.hpp"

namespace mcb::shot_evidence
{
    namespace
    {
        std::mutex g_write_mutex{};

        std::string escape_json(std::string_view value)
        {
            std::string out{};
            out.reserve(value.size() + 8);

            for (const auto ch : value)
            {
                switch (ch)
                {
                case '\\': out += "\\\\"; break;
                case '"':  out += "\\\""; break;
                case '\n': out += "\\n";  break;
                case '\r': out += "\\r";  break;
                case '\t': out += "\\t";  break;
                default:
                    if (static_cast<unsigned char>(ch) >= 0x20)
                        out += ch;
                    break;
                }
            }

            return out;
        }

        std::filesystem::path log_path()
        {
            if (const auto* local = std::getenv("LOCALAPPDATA");
                local && *local)
            {
                return std::filesystem::path(local) /
                       "MCB" / "shot_evidence.jsonl";
            }

            std::error_code ec;
            const auto temp = std::filesystem::temp_directory_path(ec);
            return (ec ? std::filesystem::current_path() : temp) /
                   "MCB" / "shot_evidence.jsonl";
        }

        void append(std::string_view line)
        {
            std::scoped_lock lock(g_write_mutex);

            const auto path = log_path();
            std::error_code ec;
            std::filesystem::create_directories(
                path.parent_path(), ec);
            if (ec)
                return;

            std::ofstream file(
                path,
                std::ios::binary |
                std::ios::out |
                std::ios::app);
            if (!file)
                return;

            file.write(
                line.data(),
                static_cast<std::streamsize>(line.size()));
            file.put('\n');
            file.flush();
        }
    }

    void request(
        std::uint64_t id,
        std::uintptr_t victim,
        int hitgroup,
        float expected_damage,
        float hitchance,
        float predicted_inaccuracy,
        float predicted_spread,
        float aim_x, float aim_y, float aim_z,
        float shoot_x, float shoot_y, float shoot_z,
        int tick,
        std::uint32_t weapon_type,
        bool forced,
        float time )
    {
        append(std::format(
            R"({{"stage":"REQUEST","id":{},"time":{},"tick":{},"victim":"0x{:X}","hitgroup":{},"expected_damage":{},"hitchance":{},"predicted_inaccuracy":{},"predicted_spread":{},"aim":[{},{},{}],"shoot":[{},{},{}],"weapon_type":{},"forced":{}}})",
            id, time, tick, victim, hitgroup,
            expected_damage, hitchance,
            predicted_inaccuracy, predicted_spread,
            aim_x, aim_y, aim_z,
            shoot_x, shoot_y, shoot_z,
            weapon_type,
            forced ? "true" : "false"));
    }

    void weapon_fire(
        std::uint64_t id,
        float time,
        bool matched )
    {
        append(std::format(
            R"({{"stage":"WEAPON_FIRE","id":{},"time":{},"matched":{}}})",
            id, time, matched ? "true" : "false"));
    }

    void server_inaccuracy(
        std::uint64_t id,
        float value,
        float time )
    {
        append(std::format(
            R"({{"stage":"SERVER_INACCURACY","id":{},"time":{},"value":{}}})",
            id, time, value));
    }

    void server_shoot_position(
        std::uint64_t id,
        float x, float y, float z,
        float time )
    {
        append(std::format(
            R"({{"stage":"SERVER_SHOOT_POS","id":{},"time":{},"pos":[{},{},{}]}})",
            id, time, x, y, z));
    }

    void impact(
        std::uint64_t id,
        float x, float y, float z,
        float time )
    {
        append(std::format(
            R"({{"stage":"IMPACT","id":{},"time":{},"pos":[{},{},{}]}})",
            id, time, x, y, z));
    }

    void hurt(
        std::uint64_t id,
        int damage,
        int health,
        int hitgroup,
        int expected_hitgroup,
        float expected_damage,
        std::string_view mismatch_reason,
        float time )
    {
        append(std::format(
            R"({{"stage":"HURT","id":{},"time":{},"damage":{},"health":{},"hitgroup":{},"expected_hitgroup":{},"expected_damage":{},"mismatch":"{}"}})",
            id, time, damage, health,
            hitgroup, expected_hitgroup,
            expected_damage,
            escape_json(mismatch_reason)));
    }

    void miss(
        std::uint64_t id,
        std::string_view reason,
        bool weapon_fire_confirmed,
        bool server_inaccuracy_confirmed,
        bool impact_confirmed,
        float time )
    {
        append(std::format(
            R"({{"stage":"MISS","id":{},"time":{},"reason":"{}","weapon_fire":{},"server_inaccuracy":{},"impact":{}}})",
            id, time, escape_json(reason),
            weapon_fire_confirmed ? "true" : "false",
            server_inaccuracy_confirmed ? "true" : "false",
            impact_confirmed ? "true" : "false"));
    }

    void discarded(
        std::uint64_t id,
        std::string_view reason,
        float time )
    {
        append(std::format(
            R"({{"stage":"DISCARDED","id":{},"time":{},"reason":"{}"}})",
            id, time, escape_json(reason)));
    }
}
