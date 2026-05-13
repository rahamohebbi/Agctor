using System;
using System.Collections.Generic;

namespace AgctorSDK.Core.ProjectMemory.OutOfSchema;

/// <summary>
/// Conservative detector for short affirmative / negative replies (PRD-019 confirmation turn).
/// Keeps the signal intentionally narrow: only pure confirmations short-circuit the pipeline,
/// so mixed messages like "yes, and also add his car" still flow through extraction.
/// </summary>
public static class ConfirmationInputDetector
{
    private static readonly HashSet<string> AffirmativeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "y", "yep", "yeah", "yup", "yess", "ya", "sure", "ok", "okay", "k",
        "confirm", "confirmed", "store", "save", "do", "do it", "go", "proceed",
        "please", "please do", "yes please", "store it", "save it", "store this fact", "save this fact",
        "store the fact", "save the fact", "store that fact", "save that fact", "sounds good",
        "correct", "right", "affirmative", "absolutely", "of course",
        "i want to save", "i want to store", "want to save", "want to store",
        "yes i want to save", "yes i want to store", "yes save",
        "consent", "i consent", "yes i consent", "yes consent", "i agree", "yes i agree"
    };

    private static readonly HashSet<string> NegativeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "n", "nope", "nah", "skip", "discard", "ignore", "cancel", "negative", "don't", "dont",
        "no thanks", "no thank you", "not now", "later"
    };

    public enum ConfirmationSignal
    {
        None = 0,
        Affirmative = 1,
        Negative = 2
    }

    /// <summary>
    /// Returns a signal only when the message is unambiguously short (<= 64 chars) and matches a known
    /// confirmation phrase. Prevents false positives on everyday "yes" mentions inside long inputs.
    /// </summary>
    public static ConfirmationSignal Classify(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return ConfirmationSignal.None;

        var normalized = Normalize(userMessage);
        if (normalized.Length == 0 || normalized.Length > 64)
            return ConfirmationSignal.None;

        if (AffirmativeTokens.Contains(normalized))
            return ConfirmationSignal.Affirmative;
        if (NegativeTokens.Contains(normalized))
            return ConfirmationSignal.Negative;

        // Natural consent variants from the dashboard ("I consent to save it", "I agree to store this").
        // Keep this after exact negative matching so "do not save" / "no thanks" never become affirmative.
        if (LooksLikeAffirmativeConsent(normalized))
            return ConfirmationSignal.Affirmative;

        return ConfirmationSignal.None;
    }

    private static bool LooksLikeAffirmativeConsent(string normalized)
    {
        var padded = " " + normalized + " ";
        if (ContainsAny(padded, " not ", " don't ", " dont ", " no ", " never "))
            return false;

        var hasConsent = ContainsAny(padded, " consent ", " agree ", " approve ", " approved ", " confirm ", " confirmed ");
        var hasSaveAction = ContainsAny(padded, " save ", " store ", " keep ");
        if (hasConsent && (hasSaveAction || ContainsAny(padded, " it ", " this ", " fact ")))
            return true;

        // Also treat short "yes ... save/store/keep" or "please save/store/keep this" as affirmative,
        // covering natural replies like "yes I wish to save it" or "yes please store this fact".
        var startsWithYes = normalized.StartsWith("yes", StringComparison.OrdinalIgnoreCase);
        var hasIntentVerb = ContainsAny(padded, " wish ", " want ", " like ", " love ", " choose ", " decide ", " please ", " go ahead ", " do it ");
        if ((startsWithYes || hasIntentVerb) && hasSaveAction)
            return true;

        return false;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string Normalize(string raw)
    {
        var span = raw.Trim();
        var buf = new System.Text.StringBuilder(span.Length);
        foreach (var c in span)
        {
            if (char.IsLetter(c) || char.IsWhiteSpace(c) || c == '\'')
                buf.Append(c);
        }

        var collapsed = System.Text.RegularExpressions.Regex.Replace(buf.ToString(), "\\s+", " ").Trim();
        return collapsed;
    }
}
