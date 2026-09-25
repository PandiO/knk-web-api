using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// An earned title/XP bracket (docs/specs/user-features/DESIGN.md §3). A user's current title is
/// resolved purely from their <see cref="User.ExperiencePoints"/> total by picking the highest
/// bracket whose <see cref="MinExperience"/> is at or below it — no separate title field is
/// persisted on <see cref="User"/>, so a title jumps straight to the correct bracket on any XP
/// change instead of needing a catch-up pass.
///
/// Real content, ported from v1's live "Titles" table export (19 brackets, id 0-18) — the
/// original placeholder seed (5 brackets, MinExperience 0/5/10/12/15) confused v1's *title-ID*
/// slot-unlock thresholds with actual XP amounts; see the
/// AddUserFeaturesPhase6RealTitleData migration for the correction.
/// </summary>
[FormConfigurableEntity("TitleBracket")]
public class TitleBracket
{
    public int Id { get; set; }

    /// <summary>Display name for a male (or gender-unset) user. See <see cref="Gender"/>.</summary>
    public string MaleName { get; set; } = null!;

    /// <summary>Display name for a female user.</summary>
    public string FemaleName { get; set; } = null!;

    /// <summary>
    /// The minimum ExperiencePoints total required to hold this title. Brackets are ordered by
    /// this value; a user's title is the highest bracket whose MinExperience is <= their XP. The
    /// upper bound is derived from the next bracket's MinExperience, not stored separately.
    /// </summary>
    public int MinExperience { get; set; }

    /// <summary>Base salary for this tier (docs/specs/user-features/DESIGN.md §5 SalaryService
    /// input), ported directly from v1's Titles.Salary column.</summary>
    public int Salary { get; set; }

    /// <summary>One-time coin bonus granted on first reaching this tier.</summary>
    public int CoinBonus { get; set; }

    /// <summary>One-time gem bonus granted on first reaching this tier.</summary>
    public int GemBonus { get; set; }

    /// <summary>One-time XP bonus granted on first reaching this tier — can itself push the user
    /// into a further bracket, which UserService.AdjustBalancesAsync's consolidation loop
    /// accounts for.</summary>
    public int ExpBonus { get; set; }

    /// <summary>Resolves the display name for the given gender (null = unset, falls back to
    /// MaleName — see User.Gender's doc comment).</summary>
    public string NameFor(Gender? gender) => gender == Gender.Female ? FemaleName : MaleName;
}
