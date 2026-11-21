using System.Diagnostics;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using CallingConventions = Reloaded.Hooks.Definitions.X86.CallingConventions;

namespace MRDX.Custom.RanchRules;

/// <summary>
///     Lower-rank tournaments: the battle menu is the calendar screen, and you enter a tournament of the current week.
///     The screen builds each month's grid (4 weeks x 6 ranks) with 0x4EDA10; after it, the tournaments of the current
///     week below the monster's rank (minus the allowed offset) are removed from the grid, so they cannot be chosen.
///     The other weeks are left as they are (the calendar keeps showing them). Free tournaments (F) are always open.
/// </summary>
public sealed class TournamentAccess
{
    // Static addresses (base 0x400000) as offsets from the start of the .exe.
    private const int BuildMonthFunction = 0x0EDA10; // builds the month grid of the calendar / battle screen
    private const int WeekCounterOffset = 0x379444;  // game week counter
    private const int MonsterRankOffset = 0x376806;  // rank of the current monster (0 = E ... 5 = S)

    // Screen object: +0x44 = month shown (1-12), +0x50 = grid [week * 6 + rank] (rank 0 = E ... 5 = S, 0xFF = none).
    private const int MonthField = 0x44, GridField = 0x50;

    /// <summary>Free tournaments (grade F, every rank): Freshmen's, Elders', Heroes', Heels', New Year.</summary>
    private static readonly HashSet<int> FreeTournaments = [0x00, 0x01, 0x03, 0x04, 0x05];

    [Function(CallingConventions.MicrosoftThiscall)]
    private delegate void BuildMonth(nint self);

    private readonly nuint _exeBase = (nuint)Process.GetCurrentProcess().MainModule!.BaseAddress;
    private readonly Func<(bool Enabled, int Offset)> _settings;
    private readonly Action<string> _log;
    private readonly IHook<BuildMonth> _hook;

    public TournamentAccess(IReloadedHooks hooks, Func<(bool Enabled, int Offset)> settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
        _hook = hooks.CreateHook<BuildMonth>(OnBuildMonth, (nint)(_exeBase + BuildMonthFunction)).Activate();
    }

    private unsafe void OnBuildMonth(nint self)
    {
        _hook.OriginalFunction(self);
        try
        {
            var (enabled, offset) = _settings();
            if (!enabled) return;
            var counter = *(uint*)(_exeBase + WeekCounterOffset);
            if (counter == uint.MaxValue) return;
            var month = (int)((counter + 12) / 4 % 12) + 1;
            var week = (int)((counter + 12) % 4);
            if (*(ushort*)(self + MonthField) != month) return; // only the month being played

            var rank = *(byte*)(_exeBase + MonsterRankOffset);
            if (rank > 5) return;
            var lowest = rank - Math.Max(0, offset); // lowest rank allowed
            var grid = (byte*)(self + GridField);
            var removed = new List<string>();
            for (var r = 0; r < lowest && r < 6; r++)
            {
                ref var cell = ref grid[week * 6 + r];
                if (cell == 0xFF || FreeTournaments.Contains(cell)) continue;
                removed.Add($"{"EDCBAS"[r]}:{cell:X2}");
                cell = 0xFF;
            }

            if (removed.Count > 0)
                _log($"Torneos de rango inferior cerrados esta semana (monstruo rango {"EDCBAS"[rank]}): {string.Join(", ", removed)}");
        }
        catch (Exception e)
        {
            _log($"No se pudo aplicar el límite de rango de torneos: {e.Message}");
        }
    }
}
