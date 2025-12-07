namespace Knutr.Abstractions.Intent;

/// <summary>
/// Recognizes intents from natural language text input.
/// </summary>
public interface IIntentRecognizer
{
    /// <summary>
    /// Attempts to recognize an actionable intent from the given text.
    /// </summary>
    /// <param name="text">The natural language input text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An IntentResult describing the recognized intent, or IntentResult.None if no intent was recognized.</returns>
    Task<IntentResult> RecognizeAsync(string text, CancellationToken ct = default);
}
