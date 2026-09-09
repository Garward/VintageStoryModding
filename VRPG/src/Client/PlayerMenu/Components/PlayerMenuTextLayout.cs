using System;
using System.Collections.Generic;
using System.Globalization;

namespace VRPG.Client.PlayerMenu.Components;

public static class PlayerMenuTextLayout
{
    public static IReadOnlyList<string> Wrap(string text, double width, Func<string, double> measure)
    {
        var lines = new List<string>(); string line = "";
        foreach (string word in text.Split(' '))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (measure(next) <= width) { line = next; continue; }
            if (line.Length > 0) { lines.Add(line); line = ""; }
            var elements = StringInfo.GetTextElementEnumerator(word);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                if (line.Length > 0 && measure(line + element) > width) { lines.Add(line); line = ""; }
                line += element;
            }
        }
        if (line.Length > 0 || lines.Count == 0) lines.Add(line);
        return lines;
    }
}
