#pragma once

#include <cstdint>
#include <string_view>

namespace mcb::shot_evidence
{
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
        float time );

    void weapon_fire(
        std::uint64_t id,
        float time,
        bool matched );

    void server_inaccuracy(
        std::uint64_t id,
        float value,
        float time );

    void server_shoot_position(
        std::uint64_t id,
        float x, float y, float z,
        float time );

    void impact(
        std::uint64_t id,
        float x, float y, float z,
        float time );

    void hurt(
        std::uint64_t id,
        int damage,
        int health,
        int hitgroup,
        int expected_hitgroup,
        float expected_damage,
        std::string_view mismatch_reason,
        float time );

    void miss(
        std::uint64_t id,
        std::string_view reason,
        bool weapon_fire_confirmed,
        bool server_inaccuracy_confirmed,
        bool impact_confirmed,
        float time );

    void discarded(
        std::uint64_t id,
        std::string_view reason,
        float time );
}
