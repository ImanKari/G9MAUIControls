namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Optional first-open heights for fit-to-content sheets, so a body the device has never shown
///     still opens at the right size.
///     <para>
///         <b>What problem this solves.</b> A fit-to-content sheet has to measure its body before it
///         knows how tall to be. On a first open there is nothing to measure yet, so the sheet opens
///         at a loading floor and grows once the platform reports a real height — visible as a small
///         settle. <see cref="G9BottomSheetHeightMemoStore" /> removes that from the second open
///         onward by remembering what it measured. These seeds remove it from the <i>first</i> open
///         too, on a fresh install.
///     </para>
///     <para>
///         <b>Entirely optional, and deliberately empty by default.</b> A seed is a measurement of
///         one specific body on one specific design — knowledge only the consuming app has. Guessing
///         on its behalf would make the settle worse, not better, so an unseeded sheet simply uses
///         the learned-height path. Seed a body only after measuring it; a wrong seed is a visible
///         jump in the opposite direction.
///     </para>
///     <example>
///         <code>
///         // MauiProgram, after measuring on a real device.
///         G9BottomSheetHeightSeeds.Seed("MyApp.Views.FilterSheetContentView", 420);
///         G9BottomSheetHeightSeeds.Seed("MyApp.Views.ConfirmSheetContentView", 210);
///         </code>
///     </example>
///     <para>
///         The key is whatever <see cref="G9BottomSheetHelper.BuildFitHeightMemoKey" /> produces for
///         a body — by default its type's full name. Log it once during development and copy the
///         value.
///     </para>
/// </summary>
public static class G9BottomSheetHeightSeeds
{
    private static readonly Dictionary<string, double> Seeds = new(StringComparer.Ordinal);

    /// <summary>
    ///     Records a first-open height, in device-independent units, for the body identified by
    ///     <paramref name="memoKey" />. Re-seeding the same key replaces the value.
    /// </summary>
    /// <param name="memoKey">
    ///     The body's memo key — see <see cref="G9BottomSheetHelper.BuildFitHeightMemoKey" />.
    /// </param>
    /// <param name="bodyHeight">
    ///     The measured natural height. Values at or below zero are ignored: a zero seed would pin
    ///     the sheet at its loading floor, which is worse than having no seed at all.
    /// </param>
    public static void Seed(string memoKey, double bodyHeight)
    {
        if (string.IsNullOrWhiteSpace(memoKey) || bodyHeight <= 0)
        {
            return;
        }

        Seeds[memoKey] = bodyHeight;
    }

    /// <summary>Looks up a seeded height. Returns <c>false</c> when the body has no seed.</summary>
    /// <remarks>
    ///     <para>
    ///         <b>Two lookups, and the second one is the one that matters.</b> The key
    ///         <see cref="G9BottomSheetHelper.BuildFitHeightMemoKey" /> produces is not the identity alone
    ///         — it is <c>identity|w{width}|{culture}|fs{fontScale}</c>, because the LEARNED store is
    ///         persisted and all three of those can change between sessions.
    ///     </para>
    ///     <para>
    ///         Seeds are compiled constants, so keying them the same way would mean one seed per
    ///         width × culture × font-scale combination — thousands of entries to cover a phone range, and
    ///         a plain <c>Seed("MyApp.FilterSheet", 420)</c> (exactly what this type's own example shows)
    ///         would silently never match. So an exact hit is tried first, letting a consumer pin one
    ///         device profile deliberately, and otherwise the device components are stripped back to the
    ///         identity.
    ///     </para>
    ///     <para>
    ///         Stripping is from the END, three separators, because an identity may itself contain
    ///         <c>|</c> — a factory body's <see cref="G9BottomSheetOptions.HeightMemoKey" /> often does.
    ///     </para>
    /// </remarks>
    public static bool TryGet(string memoKey, out double bodyHeight)
    {
        bodyHeight = 0;
        if (string.IsNullOrWhiteSpace(memoKey))
        {
            return false;
        }

        if (Seeds.TryGetValue(memoKey, out bodyHeight))
        {
            return true;
        }

        var identityEnd = memoKey.Length;
        for (var i = 0; i < 3; i++)
        {
            identityEnd = memoKey.LastIndexOf('|', identityEnd - 1);
            if (identityEnd <= 0)
            {
                bodyHeight = 0;
                return false;
            }
        }

        return Seeds.TryGetValue(memoKey[..identityEnd], out bodyHeight);
    }

    /// <summary>Removes every seed. Intended for tests.</summary>
    public static void Reset() => Seeds.Clear();
}
