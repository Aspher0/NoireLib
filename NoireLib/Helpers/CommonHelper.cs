using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// Helper class with common utility methods.
/// </summary>
public static class CommonHelper
{
    /// <summary>
    /// Executes the provided action safely, catching and logging any exceptions that occur.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    public static void ExecuteSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "An error occurred while executing a safe action.");
        }
    }

    /// <summary>
    /// Generates a new GUID string with optional hyphen ("-") removal.
    /// </summary>
    /// <param name="removeHyphens">If true, removes hyphens from the GUID string.</param>
    /// <returns>The generated GUID string.</returns>
    public static string GenerateGuidString(bool removeHyphens = false)
    {
        return removeHyphens ? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Generates a random string based on specified criteria.
    /// </summary>
    /// <param name="length">The desired length of the random string. Can change based on <paramref name="moreEntropy"/></param>
    /// <param name="moreEntropy">Adds more entropy to the string by prepending a unique prefix based on a GUID. This will increase the length of the string by 10 characters.</param>
    /// <param name="lowercase">Defines if lowercase letters should be included.</param>
    /// <param name="uppercase">Defines if uppercase letters should be included.</param>
    /// <param name="digits">Defines if digits should be included.</param>
    /// <param name="special">Defines if special characters should be included. Special characters includes "-_#@~|[]{}=+".</param>
    /// <returns>A randomly generated string.</returns>
    public static string GenerateRandomString(int length = 50, bool moreEntropy = false, bool lowercase = true, bool uppercase = true, bool digits = true, bool special = true)
    {
        length = (length <= 0) ? 50 : length;

        List<char> allowedChars = new List<char>();
        if (lowercase)
            allowedChars.AddRange("abcdefghijklmnopqrstuvwxyz".ToCharArray());
        if (uppercase)
            allowedChars.AddRange("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());
        if (digits)
            allowedChars.AddRange("0123456789".ToCharArray());
        if (special)
            allowedChars.AddRange("-_#@~|[]{}=+".ToCharArray());

        var random = new Random();
        var result = new StringBuilder(length);

        if (moreEntropy)
        {
            string uniquePrefix = GenerateGuidString(true).Substring(0, 10);
            result.Append(uniquePrefix);
        }

        for (int i = result.Length; i < length; i++)
        {
            int index = random.Next(allowedChars.Count);
            result.Append(allowedChars[index]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Opens the specified URL in the default web browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to open URL {url} in the default browser.");
        }
    }
}
