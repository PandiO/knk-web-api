using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// An earned title/XP bracket (docs/specs/user-features/DESIGN.md §3). A user's current title is
/// resolved purely from their <see cref="User.ExperiencePoints"/> total by picking the highest
/// bracket whose <see cref="MinExperience"/> is at or below it — no separate title field is
/// persisted on <see cref="User"/>, so a title jumps straight to the correct bracket on any XP
/// change instead of needing a catch-up pass.
///
/// Seeded content only, ported from v1's reward-threshold crossing points (5/10/12/15, confirmed
/// DESIGN.md §7 item 10) — v1's actual per-title names/salary/bonus values were never committed
/// to source (Titles.java issued raw SQL against a live "Titles" table with no seed data in the
/// archive), so <see cref="Name"/> below is placeholder content only, retunable later via the
/// admin UI once it exists.
/// </summary>
[FormConfigurableEntity("TitleBracket")]
public class TitleBracket
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>
    /// The minimum ExperiencePoints total required to hold this title. Brackets are ordered by
    /// this value; a user's title is the highest bracket whose MinExperience is <= their XP.
    /// </summary>
    public int MinExperience { get; set; }
}
