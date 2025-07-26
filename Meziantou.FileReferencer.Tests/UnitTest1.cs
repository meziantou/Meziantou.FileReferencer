using System.Runtime.CompilerServices;
using Meziantou.Framework;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.FileReferencer.Tests;

public class UnitTest1
{
    [Theory]
    [InlineData("a.cs", "// reference:ref1.txt", "// endreference")]
    [InlineData("a.cs", "// reference:ref1.txt", "// endref")]
    [InlineData("a.cs", "// ref:ref1.txt", "// endreference")]
    [InlineData("a.cs", "// ref:ref1.txt", "// endref")]
    [InlineData("a.js", "// ref:ref1.txt", "// endref")]
    [InlineData("a.js", "// ref:ref1.txt", "/* endref */")]
    [InlineData("a.js", "/* ref:ref1.txt */", "/* endref */")]
    [InlineData("a.ts", "/* ref:ref1.txt */", "/* endref */")]
    [InlineData("a.css", "/* ref:ref1.txt */", "/* endref */")]
    [InlineData("a.xml", "<!-- ref:ref1.txt -->", "<!-- endref -->")]
    [InlineData("a.htm", "<!-- ref:ref1.txt -->", "<!-- endref -->")]
    [InlineData("a.html", "<!-- ref:ref1.txt -->", "<!-- endref -->")]
    [InlineData("a.yml", "# ref:ref1.txt", "# endref")]
    [InlineData("a.yaml", "# ref:ref1.txt", "# endref")]
    [InlineData("dockerfile", "# ref:ref1.txt", "# endref")]
    [InlineData("a.txt", "/* ref:ref1.txt */", "/* endref */")]
    [InlineData("a.sql", "-- ref:ref1.txt", "-- endref")]
    public async Task Update(string fileName, string start, string end)
    {
        await Test.SnapshotFile(fileName, $"""
            {start}
            {end}
            """, $"""
            {start}
            ref1
            {end}
            """);
    }

    [Fact]
    public async Task DoesNotUpdateRefOnMultipleLines()
    {
        await Test.DoesNotUpdate("test.cs", """
            // ref:
            ref1.txt
            // endref
            """);
    }

    [Fact]
    public async Task CSharpNestedRegions()
    {
        await Test.SnapshotFile("test.cs", """
            #region ref:ref1.txt
            line1
            #region
            line2
            #endregion
            line3
            #endregion

            // endref
            """, """
            #region ref:ref1.txt
            ref1
            #endregion

            // endref
            """);
    }

    [Fact]
    public async Task JsonPreserveIndentation()
    {
        await Test.SnapshotFile("test.json", """
            {
              // ref:ref1.json
              // endref
            }
            """, """
            {
              // ref:ref1.json
              {
                "key": "value"
              }
              // endref
            }
            """);
    }

    [Fact]
    public async Task ParseOptions()
    {
        await Test.SnapshotFile("test.cs", """
                // ref:ref1.txt;indent=false;eol=lf
                // endref
            """, """
                // ref:ref1.txt;indent=false;eol=lf
            ref1
                // endref
            """);
    }

    [Fact]
    public async Task FileReferences_LocalFile()
    {
        await using TemporaryDirectory temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("refs/LICENSE.txt", "dummy");
        temporaryDirectory.CreateTextFile("FileReferences.json", """
            {
                "references": {
                    "LICENSE.txt": {
                        "ref": "./refs/LICENSE.txt"
                    }
                }
            }
            """);

        Test.Run(temporaryDirectory.FullPath);
        var actualContent = File.ReadAllText(temporaryDirectory.GetFullPath("LICENSE.txt"));
        Assert.Equal("dummy", actualContent);
    }

    [Fact]
    public async Task FileReferences_Remote()
    {
        await using TemporaryDirectory temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("FileReferences.json", """
            {
                "references": {
                    "LICENSE.txt": {
                        "ref": "https://raw.githubusercontent.com/meziantou/Meziantou.Framework/a0669411207ee172a609950a356cc1508e662e0e/LICENSE.txt"
                    }
                }
            }
            """);

        Test.Run(temporaryDirectory.FullPath);
        var actualContent = File.ReadAllText(temporaryDirectory.GetFullPath("LICENSE.txt"));
        Assert.Equal("""
            MIT License

            Copyright (c) Gérald Barré

            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in all
            copies or substantial portions of the Software.

            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
            AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
            SOFTWARE.

            """, actualContent, ignoreLineEndingDifferences: true);
    }

    private sealed class Test
    {
        public static void Run(params string[] arguments)
        {
            var result = (int)typeof(Program).Assembly.EntryPoint!.Invoke(null, [arguments])!;
            Assert.Equal(0, result);
        }

        public static async Task<string> UpdateFile(string fileName, string content)
        {
            await using TemporaryDirectory temporaryDirectory = TemporaryDirectory.Create();
            temporaryDirectory.CreateTextFile("ref1.txt", "ref1");
            temporaryDirectory.CreateTextFile("ref1.json", """
                {
                  "key": "value"
                }
                """);
            var path = temporaryDirectory.CreateTextFile(fileName, content);
            Run(path);
            return await File.ReadAllTextAsync(path);
        }

        public static async Task DoesNotUpdate(string fileName, string content)
        {
            var newContent = await UpdateFile(fileName, content);
            Assert.Equal(content, newContent);
        }

        [InlineSnapshotAssertion(nameof(expected))]
        public static async Task SnapshotFile(string fileName, string content, string? expected = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
        {
            var newContent = await UpdateFile(fileName, content);
            InlineSnapshot.Validate(newContent, expected, filePath, lineNumber);
        }
    }
}
