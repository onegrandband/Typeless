using System.Text.RegularExpressions;

namespace AutoTyper.Core;

public static class PromptParser
{
    /// <summary>
    /// Given raw OCR text, tries to find the word/phrase the game/app is asking the user to type.
    /// Examples: "Type: hello", "type this: hello world", "Type hello to continue", "[this_word]"
    /// </summary>
    public static string ExtractTypeThisWord(string ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return string.Empty;

        // Normalize common OCR artifacts and whitespace
        var normalized = ocrText.Replace('“', '"').Replace('”', '"').Replace('‘', '\'').Replace('’', '\'').Trim();

        // Pattern 1: "Type: word" / "Type this: word" / "Type the word: word" etc.
        var match = Regex.Match(normalized, @"type\s*(?:this|the\s+word|the\s+phrase|that)?\s*[:\-=]?\s*[""']?([^""'\r\n]+)[""']?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var value = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return CleanWord(value);
        }

        // Pattern 2: Bracket placeholders like [hello], [this_word]
        match = Regex.Match(normalized, @"\[([A-Za-z0-9_\-\s]+)\]", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return CleanWord(match.Groups[1].Value);
        }

        // Pattern 3: Just grab the first reasonable word/phrase in the text if no clear prompt was found
        // This prevents random screen text from being used when there is no type prompt.
        var firstLine = normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            var words = firstLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
            {
                // Prefer a token that looks like the prompt answer: letters/numbers, not UI labels
                foreach (var w in words)
                {
                    var cleaned = CleanWord(w);
                    if (cleaned.Length >= 2 && cleaned.All(char.IsLetterOrDigit))
                        return cleaned;
                }
            }
        }

        return string.Empty;
    }

    private static string CleanWord(string input)
    {
        return new string(input.Trim().Where(c => !char.IsPunctuation(c) || c == '_' || c == '-').ToArray()).Trim();
    }
}
