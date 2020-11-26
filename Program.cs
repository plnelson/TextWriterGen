// Copyright (c) 2016 Patrick Nelson. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TextWriterGen
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 4)
            {
                Console.WriteLine("TextWriterGen");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("TextWriterGen <namespace name> <class name> <input file> <output file>");

                return;
            }

            string @namespace = args[0];
            string @class = args[1];
            string input = args[2];
            string output = args[3];
            string inputName = Path.GetFileName(input);

            if (!File.Exists(input))
            {
                Console.WriteLine($"Input file {input} was not found.");
                return;
            }

            using (StreamReader reader = File.OpenText(input))
            {
                using (StreamWriter writer = File.CreateText(output))
                {
                    GenerateTextWriter(inputName, @namespace, @class, reader, writer);
                    writer.Flush();
                }
            }

            Console.WriteLine("Output file generated.");
        }

        static void GenerateTextWriter(string inputName, string @namespace, string @class, TextReader reader, TextWriter writer)
        {
            bool outputEnabled = false;
            string[] parameters = null;
            int lineNum = 0;

            // Write the prolog
            CodeText text = new CodeText(writer);
            text.Prolog(inputName, @namespace, @class);

            // Now write the contents

            string inputText = reader.ReadLine();
            while (inputText != null)
            {
                lineNum++;
                if (inputText.StartsWith("%$"))
                {
                    if (inputText.StartsWith("%$-"))
                    {
                        bool parsedFunction = TryParseFunctionName(inputText, lineNum, out string functionName, out parameters);
                        if (parsedFunction)
                        {
                            if (outputEnabled)
                            {
                                // Terminate the previous function
                                text.FunctionEnd();
                            }

                            text.FunctionStart(functionName, ConvertParametersToCodeText(parameters));
                            outputEnabled = true;
                        }
                    }
                    // else ignore anything else starting with %$
                }
                else if (outputEnabled)
                {
                    string[] substitutedText = SubstituteText(lineNum, inputText, parameters);

                    if (substitutedText == null || substitutedText.Length == 0)
                    {
                        text.EmptyLine();
                    }
                    else if (substitutedText.Length == 1)
                    {
                        text.LiteralLine(substitutedText[0]);
                    }
                    else
                    {
                        text.SubstitutedLine(string.Join(", ", substitutedText));
                    }
                }

                inputText = reader.ReadLine();
            }

            if (outputEnabled)
            {
                // Terminate the last function
                text.FunctionEnd();
            }

            // Write the epilog
            text.Epilog();
        }

        static bool TryParseFunctionName(string input, int lineNum, out string functionName, out string[] parameters)
        {
            functionName = null;
            parameters = null;

            int readIndex = 3; // Skip "%$-" characters
            SkipWhite(input, ref readIndex);
            functionName = ReadIdentifier(input, ref readIndex);
            if (string.IsNullOrEmpty(functionName))
                return false;

            // Look for parameters
            SkipWhite(input, ref readIndex);
            if (Accept(input, '(', ref readIndex))
            {
                if (!Accept(input, ')', ref readIndex))
                {
                    List<string> paramList = new List<string>();
                    do
                    {
                        SkipWhite(input, ref readIndex);
                        string param = ReadIdentifier(input, ref readIndex);
                        if (string.IsNullOrEmpty(param))
                        {
                            ParseError(lineNum, @"Expecting parameter name identifier.");
                            return false;
                        }

                        SkipWhite(input, ref readIndex);

                        paramList.Add(param);
                    }
                    while (Accept(input, ',', ref readIndex));

                    if (!Accept(input, ')', ref readIndex))
                    {
                        ParseError(lineNum, @"Expecting ')'.");
                        return false;
                    }

                    parameters = paramList.ToArray();
                }
            }

            SkipWhite(input, ref readIndex);
            if (readIndex < input.Length)
                ParseError(lineNum, "Unexpected text after function name");

            return true;
        }

        static bool Accept(string input, char c, ref int readIndex)
        {
            if (readIndex >= input.Length)
                return false;

            if (input[readIndex] == c)
            {
                readIndex++;
                return true;
            }

            return false;
        }

        static void SkipWhite(string input, ref int readIndex)
        {
            while (readIndex < input.Length)
            {
                char c = input[readIndex];
                if (c != ' ' && c != '\t')
                    break;

                readIndex++;
            }
        }

        static string ReadIdentifier(string input, ref int readIndex)
        {
            if (readIndex >= input.Length)
                return null;

            char c = input[readIndex];

            // First character must be a-z.
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z'))
                return null;

            // The rest of the characters can be a-z or 0-9.
            int identLen = 1;
            while ((readIndex + identLen) < input.Length)
            {
                c = input[readIndex + identLen];
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9'))
                    break;

                identLen++;
            }

            string result = input.Substring(readIndex, identLen);
            readIndex += identLen;

            return result;
        }

        static string[] SubstituteText(int lineNum, string input, string[] parameters)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            if (parameters == null || parameters.Length == 0)
            {
                // Don't need to substitue parameters
                return new string[] { EscapeQuotes(input) };
            }

            int delimStartIndex = input.IndexOf('$');
            if (delimStartIndex == -1)
            {
                // Nothing to substitute on this line
                return new string[] { EscapeQuotes(input) };
            }

            List<string> resultList = new List<string>();
            int lastOutputIndex = -1;
            while (delimStartIndex != -1 && delimStartIndex < input.Length)
            {
                if (delimStartIndex > lastOutputIndex)
                {
                    resultList.Add(WrapString(input.Substring(lastOutputIndex + 1, (delimStartIndex - lastOutputIndex - 1))));
                }

                int delimEndIndex = input.IndexOf('$', delimStartIndex + 1);
                int len = delimEndIndex - delimStartIndex - 1;
                if (len == 0)
                {
                    // Escaped '$'
                    resultList.Add(@"@""$""");
                }
                else
                {
                    string param = input.Substring(delimStartIndex + 1, len);
                    if (parameters.Contains(param))
                        resultList.Add("@" + param);
                    else
                        ParseError(lineNum, $"Unknown parameter - {param}");
                }

                lastOutputIndex = delimEndIndex;
                delimStartIndex = input.IndexOf('$', lastOutputIndex + 1);
            }

            if ((lastOutputIndex + 1) < input.Length)
            {
                resultList.Add(WrapString(input.Substring(lastOutputIndex + 1, (input.Length - lastOutputIndex - 1))));
            }

            return resultList.Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }

        static string WrapString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            return $"@\"{EscapeQuotes(input)}\"";
        }

        static string EscapeQuotes(string input)
        {
            return input.Replace("\"", "\"\"");
        }

        static string ConvertParametersToCodeText(string[] parameters)
        {
            if (parameters == null)
                return string.Empty;

            string[] parametersWithTypes = parameters.Select(s => $"string @{s}").ToArray();
            return string.Join(", ", parametersWithTypes);
        }

        static void ParseError(int lineNum, string message)
        {
            Console.WriteLine($"Parse error on line {lineNum}: {message}");
        }
    }
}
