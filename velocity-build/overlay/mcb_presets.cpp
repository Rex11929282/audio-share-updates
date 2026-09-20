#include <pch/pch.hpp>

#include <cmath>
#include <cstdlib>
#include <unordered_set>

#include <core/settings.hpp>
#include <external/config.hpp>

#include "mcb_presets.hpp"

namespace mcb::presets
{
    namespace
    {
        using key_set = std::unordered_set<std::uint32_t>;

        constexpr const char* rage_groups[]{
            "ragebot - pistol",
            "ragebot - smg",
            "ragebot - rifle",
            "ragebot - shotgun",
            "ragebot - sniper",
            "ragebot - lmg"
        };

        constexpr const char* legit_groups[]{
            "legitbot - pistol",
            "legitbot - smg",
            "legitbot - rifle",
            "legitbot - shotgun",
            "legitbot - sniper",
            "legitbot - lmg"
        };

        void allow(
            key_set& keys,
            std::string_view category,
            std::string_view name)
        {
            keys.insert(config::detail::make_key(category, name));
        }

        void set_setting(
            xui::setting& setting,
            bool value,
            key_set& keys)
        {
            allow(keys, setting.category, setting.name);
            setting.value = value;

            if (setting.bind.key != 0)
            {
                if (setting.bind.mode == xui::bind_mode::toggle)
                    setting.bind.active = value;
                else if (!value)
                    setting.bind.active = false;
            }
        }

        template <typename T>
        void set_value(
            config::val<T>& setting,
            T value,
            std::string_view category,
            std::string_view name,
            key_set& keys)
        {
            allow(keys, category, name);
            setting = value;
        }

        template <typename E>
        void set_enum(
            config::enm<E>& setting,
            E value,
            std::string_view category,
            std::string_view name,
            key_set& keys)
        {
            allow(keys, category, name);
            setting = value;
        }

        template <std::uint32_t N>
        void set_bools(
            config::bools<N>& setting,
            std::initializer_list<bool> values,
            std::string_view category,
            std::string_view name,
            key_set& keys)
        {
            allow(keys, category, name);

            std::size_t index{};
            for (const auto value : values)
            {
                if (index >= N)
                    break;
                setting[index++] = value;
            }
        }

        void apply_legit(key_set& keys)
        {
            auto& combat = settings::g_combat;
            set_setting(combat.m_legitbot.enabled, true, keys);
            set_setting(combat.m_ragebot.enabled, false, keys);
            set_setting(combat.m_antiaim.enabled, false, keys);

            for (std::size_t i{}; i < combat.m_legitbot.groups.size(); ++i)
            {
                auto& group = combat.m_legitbot.groups[i];
                const auto category = std::string_view{legit_groups[i]};

                // Keep the source's hold-to-use semantics for aimbot/triggerbot.
                set_setting(group.aimbot, false, keys);
                set_value(group.fov, 5.0f, category, "fov", keys);
                set_value(group.smooth, 5, category, "smooth", keys);
                set_bools(group.hitboxes,
                          {true, false, false, false, false},
                          category, "hitboxes", keys);

                set_setting(group.rcs, true, keys);
                set_value(group.rcs_min, 95, category, "rcs min", keys);
                set_value(group.rcs_max, 105, category, "rcs max", keys);

                set_setting(group.standalone_rcs, false, keys);
                set_value(group.standalone_rcs_strength, 100,
                          category, "standalone rcs strength", keys);
                set_value(group.standalone_rcs_min, 95,
                          category, "standalone rcs min", keys);
                set_value(group.standalone_rcs_max, 105,
                          category, "standalone rcs max", keys);

                set_setting(group.triggerbot, false, keys);
                set_value(group.trigger_delay, 5,
                          category, "trigger delay", keys);
                set_value(group.trigger_hitchance, 80,
                          category, "trigger hitchance", keys);
                set_setting(group.trigger_head_only, false, keys);
                set_setting(group.give_me_your_seed, false, keys);

                set_setting(group.autowall, true, keys);
                set_value(group.min_damage, 101,
                          category, "min damage", keys);
                set_setting(group.visualize_fov, true, keys);
            }
        }

        void apply_rage_common(key_set& keys, bool hvh)
        {
            auto& combat = settings::g_combat;
            set_setting(combat.m_legitbot.enabled, false, keys);
            set_setting(combat.m_ragebot.enabled, true, keys);
            set_setting(combat.m_antiaim.enabled, hvh, keys);

            for (std::size_t i{}; i < combat.m_ragebot.groups.size(); ++i)
            {
                auto& group = combat.m_ragebot.groups[i];
                const auto category = std::string_view{rage_groups[i]};

                set_setting(group.silent, true, keys);
                set_setting(group.no_spread, false, keys);
                set_setting(group.doubletap, hvh, keys);
                set_setting(group.body_aim, false, keys);
                set_setting(group.force_shot_air, false, keys);
                set_setting(group.force_shot, false, keys);

                set_value(group.max_fov, 180.0f,
                          category, "max fov", keys);
                set_value(group.hitchance, 80,
                          category, "hit chance", keys);
                set_value(group.min_damage, 101,
                          category, "min damage", keys);

                set_value(group.min_damage_override_value, 11,
                          category, "min damage override value", keys);
                set_setting(group.min_damage_override, false, keys);

                set_value(group.hitchance_override_value, 75,
                          category, "hit chance override value", keys);
                set_setting(group.hitchance_override, false, keys);

                set_value(group.pointscale, 85.0f,
                          category, "point scale", keys);
                set_setting(group.dynamic_pointscale, true, keys);
                set_setting(group.debug_multipoints, false, keys);
                set_bools(group.hitboxes,
                          {true, true, true, true, true, true},
                          category, "hitboxes", keys);
            }

            set_value(combat.m_lagcomp.max_backtrack_ticks, 12,
                      "ragebot", "max backtrack ticks", keys);
            set_setting(combat.m_lagcomp.extrapolation, true, keys);
            set_value(combat.m_lagcomp.max_extrapolate_ticks, 8,
                      "ragebot", "max extrapolate ticks", keys);

            if (!hvh)
                return;

            auto& aa = combat.m_antiaim;
            set_enum(aa.pitch,
                     settings::combat::antiaim::pitch_mode::down,
                     "anti aim", "pitch", keys);
            set_setting(aa.auto_yaw_adjust, true, keys);
            set_setting(aa.manual_left, false, keys);
            set_setting(aa.manual_right, false, keys);
            set_setting(aa.hide_shots, true, keys);
            set_setting(aa.avoid_backstab, true, keys);
            set_setting(aa.direction_indicator, true, keys);
            set_setting(aa.direction_indicator_glow, true, keys);
            set_value(aa.direction_indicator_glow_strength, 0.55f,
                      "anti aim", "direction indicator glow strength", keys);

            settings::finalize_binds();
        }

        void apply(kind value, key_set& keys)
        {
            switch (value)
            {
            case kind::legit:
                apply_legit(keys);
                break;
            case kind::rage:
                apply_rage_common(keys, false);
                break;
            case kind::hvh:
                apply_rage_common(keys, true);
                break;
            }
        }

        bool verify_untouched(
            const nlohmann::json& before,
            const nlohmann::json& after,
            const key_set& allowed,
            std::string& error)
        {
            if (!before.contains("fields") ||
                !after.contains("fields") ||
                !before["fields"].is_object() ||
                !after["fields"].is_object())
            {
                error = "CFG verification failed: missing fields object";
                return false;
            }

            const auto& before_fields = before["fields"];
            const auto& after_fields = after["fields"];

            if (before_fields.size() != after_fields.size())
            {
                error = "CFG verification failed: registry field count changed";
                return false;
            }

            for (auto it = before_fields.begin();
                 it != before_fields.end(); ++it)
            {
                char* end{};
                const auto parsed = std::strtoul(
                    it.key().c_str(), &end, 16);
                if (!end || *end != '\0')
                {
                    error = "CFG verification failed: invalid registry key";
                    return false;
                }

                const auto key = static_cast<std::uint32_t>(parsed);
                if (allowed.contains(key))
                    continue;

                const auto found = after_fields.find(it.key());
                if (found == after_fields.end() ||
                    *found != it.value())
                {
                    error =
                        "CFG verification failed: non-preset field changed";
                    return false;
                }
            }

            return true;
        }

        bool verify_mode(kind value, std::string& error)
        {
            const auto& combat = settings::g_combat;
            const auto expected_hvh = value == kind::hvh;
            const auto expected_rage =
                value == kind::rage || expected_hvh;
            const auto expected_legit = value == kind::legit;

            if (combat.m_legitbot.enabled.value != expected_legit ||
                combat.m_ragebot.enabled.value != expected_rage ||
                combat.m_antiaim.enabled.value != expected_hvh)
            {
                error = "CFG verification failed: mode exclusivity mismatch";
                return false;
            }

            if (expected_legit)
            {
                for (const auto& group : combat.m_legitbot.groups)
                {
                    if (std::fabs(group.fov.value - 5.0f) > 0.001f ||
                        group.smooth.value != 5 ||
                        !group.rcs.value ||
                        group.triggerbot.value ||
                        group.hitboxes[0] != true)
                    {
                        error = "CFG verification failed: legit group mismatch";
                        return false;
                    }

                    for (std::size_t i = 1; i < 5; ++i)
                    {
                        if (group.hitboxes[i])
                        {
                            error =
                                "CFG verification failed: legit hitbox mismatch";
                            return false;
                        }
                    }
                }
                return true;
            }

            for (const auto& group : combat.m_ragebot.groups)
            {
                if (!group.silent.value ||
                    group.no_spread.value ||
                    group.doubletap.value != expected_hvh ||
                    group.hitchance.value != 80 ||
                    group.min_damage.value != 101 ||
                    std::fabs(group.pointscale.value - 85.0f) > 0.001f ||
                    !group.dynamic_pointscale.value)
                {
                    error = "CFG verification failed: rage group mismatch";
                    return false;
                }

                for (std::size_t i{}; i < 6; ++i)
                {
                    if (!group.hitboxes[i])
                    {
                        error =
                            "CFG verification failed: rage hitbox mismatch";
                        return false;
                    }
                }
            }

            if (expected_hvh)
            {
                const auto& aa = combat.m_antiaim;
                if (!aa.auto_yaw_adjust.value ||
                    !aa.hide_shots.value ||
                    !aa.avoid_backstab.value ||
                    aa.manual_left.value ||
                    aa.manual_right.value ||
                    aa.pitch.value !=
                        settings::combat::antiaim::pitch_mode::down)
                {
                    error =
                        "CFG verification failed: HVH anti-aim mismatch";
                    return false;
                }
            }

            return true;
        }

        bool rollback(
            const nlohmann::json& snapshot)
        {
            if (!config::from_json(snapshot))
                return false;
            settings::finalize_binds();
            return config::to_json() == snapshot;
        }
    }

    const wchar_t* registry_name(kind value) noexcept
    {
        switch (value)
        {
        case kind::legit: return L"MCB Legit";
        case kind::rage:  return L"MCB Rage";
        case kind::hvh:   return L"MCB HVH";
        }
        return L"MCB Preset";
    }

    result apply_and_save(kind value)
    {
        result out{};
        const auto snapshot = config::to_json();
        key_set allowed{};
        std::string error{};

        apply(value, allowed);
        settings::finalize_binds();

        const auto applied = config::to_json();
        if (!verify_mode(value, error) ||
            !verify_untouched(snapshot, applied, allowed, error))
        {
            out.rollback_ok = rollback(snapshot);
            out.message = error;
            if (!out.rollback_ok)
                out.message += "; rollback verification FAILED";
            return out;
        }

        const auto* name = registry_name(value);
        if (!config::registry::save(name))
        {
            out.rollback_ok = rollback(snapshot);
            out.message = "Native CFG save failed";
            if (!out.rollback_ok)
                out.message += "; rollback verification FAILED";
            return out;
        }

        // Re-load the just-written FULL registry config and compare every
        // registered field against the state that was saved.
        if (!config::registry::load(name) ||
            config::to_json() != applied)
        {
            config::registry::remove(name);
            out.rollback_ok = rollback(snapshot);
            out.message = "Native CFG readback verification failed";
            if (!out.rollback_ok)
                out.message += "; rollback verification FAILED";
            return out;
        }

        out.success = true;
        out.saved = true;
        out.rollback_ok = true;

        switch (value)
        {
        case kind::legit:
            out.message =
                "MCB Legit: PARTIAL preset applied; FULL native CFG saved";
            break;
        case kind::rage:
            out.message =
                "MCB Rage: PARTIAL preset applied; FULL native CFG saved";
            break;
        case kind::hvh:
            out.message =
                "MCB HVH: PARTIAL preset applied; FULL native CFG saved";
            break;
        }

        return out;
    }
}
