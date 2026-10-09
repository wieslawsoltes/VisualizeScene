using System.Globalization;
using VisualizeScene.Rendering.Skia;

var passed = 0;
void Test(string name, string input, double width, string expected)
{
    var result = GraphRenderer.Ellipsize(input, width, text => StringInfo.ParseCombiningCharacters(text).Length * 10);
    if (result != expected) throw new Exception($"{name}: expected '{expected}', got '{result}'");
    Console.WriteLine("PASS " + name); passed++;
}
Test("No truncation", "abc", 30, "abc");
Test("Zero width", "abc", 0, "");
Test("Sub-glyph width terminates", "abc", 1, "");
Test("Ellipsis only", "abc", 10, "…");
Test("One character and ellipsis", "abcdef", 20, "a…");
Test("Two characters and ellipsis", "abcdef", 30, "ab…");
Test("Combining marks are not split", "a\u0301bcd", 20, "a\u0301…");
Test("Emoji surrogate pairs are not split", "😀😃😄😁", 20, "😀…");
Test("Empty string", "", 100, "");
Test("Unbounded width", "text", double.PositiveInfinity, "text");
Console.WriteLine($"{passed}/{passed} text layout tests passed");
