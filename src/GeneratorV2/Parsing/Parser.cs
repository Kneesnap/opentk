using GeneratorV2.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace GeneratorV2.Parsing
{
    public class Parser
    {
        public static Specification Parse(Stream input)
        {
            var xdocument = XDocument.Load(input);

            var commands = ParseCommands(xdocument.Root!);
            var enums =  ParseEnums(xdocument.Root!);

            var features = ParseFeatures(xdocument.Root!, commands, enums);
            var extensions = ParseExtensions(xdocument.Root!, commands, enums);

            return new Specification(commands, enums, features, extensions);
        }


        public static Dictionary<string, Command2> ParseCommands(XElement input)
        {
            Logger.Info("Begining parsing of commands.");
            var xelement = input.Element("commands")!;

            var commands = new Dictionary<string, Command2>();
            foreach (var element in xelement.Elements("command"))
            {
                var command = ParseCommand(element);
                commands.Add(command.Method.EntryPoint, command);
            }

            return commands;
        }

        private static Command2 ParseCommand(XElement c)
        {
            var proto = c.Element("proto");
            if (proto == null) throw new Exception("Missing proto tag!");

            var name = proto.Element("name")?.Value;
            if (name == null) throw new Exception("Missing name tag!");

            var parameterList = new List<Parameter2>();
            foreach (var element in c.Elements("param"))
            {
                var paramName = element.Element("name")?.Value;
                var ptype = ParsePType(element);

                if (paramName == null) throw new Exception("Missing parameter name!");

                //isGLhandleArb |= ptype.Name == PlatformSpecificGlHandleArbFlag;
                parameterList.Add(new Parameter2(ptype, paramName));
            }

            var returnType = ParsePType(proto);

            var method = new GLMethod(returnType, name, parameterList.ToArray());

            // TODO: We can skip the Command class and just use Method directly.
            return new Command2(method, name);
        }

        private static IExpression ParseExpression(string expression)
        {
            var retExpr = ParseExpressionPrio2(expression, out string leftOver);

            if (string.IsNullOrEmpty(leftOver) == false)
                throw new Exception($"Failed to parse expression '{expression}' with leftover '{leftOver}'");

            return retExpr;
        }

        private static IExpression ParseExpressionPrio2(string expression, out string leftOver)
        {
            var retExpr = ParseExpressionPrio1(expression, out string exp);
            exp = exp.TrimStart();

            BinaryOperator op;
            while ((op = GetOperation(exp)) != BinaryOperator.Invalid)
            {
                exp = exp[1..];
                var right = ParseExpressionPrio1(exp, out exp);
                exp = exp.TrimStart();

                retExpr = new BinaryOperation(retExpr, op, right);
            }

            leftOver = exp;
            return retExpr;

            static BinaryOperator GetOperation(string expression)
            {
                if (expression.Length == 0) return BinaryOperator.Invalid;
                return expression[0] switch
                {
                    '+' => BinaryOperator.Addition,
                    '-' => BinaryOperator.Subtraction,
                    _ => BinaryOperator.Invalid,
                };
            }
        }

        private static IExpression ParseExpressionPrio1(string expression, out string leftOver)
        {
            var retExpr = ParseExpressionPrio0(expression, out string exp);
            exp = exp.TrimStart();

            BinaryOperator op;
            while ((op = GetOperation(exp)) != BinaryOperator.Invalid)
            {
                exp = exp[1..];
                var right = ParseExpressionPrio0(exp, out exp);
                exp = exp.TrimStart();

                retExpr = new BinaryOperation(retExpr, op, right);
            }

            leftOver = exp;
            return retExpr;

            static BinaryOperator GetOperation(string expression)
            {
                if (expression.Length == 0) return BinaryOperator.Invalid;
                return expression[0] switch
                {
                    '*' => BinaryOperator.Multiplication,
                    '/' => BinaryOperator.Division,
                    '%' => BinaryOperator.Modulo,
                    _ => BinaryOperator.Invalid,
                };
            }
        }

        private static IExpression ParseExpressionPrio0(string expression, out string leftOver)
        {
            expression = expression.TrimStart();
            if (expression.StartsWith("COMPSIZE("))
            {
                var exp = expression["COMPSIZE(".Length..];
                List<IExpression> arguments = new List<IExpression>();
                while (exp[0] != ')')
                {
                    arguments.Add(ParseExpressionPrio2(exp, out exp));

                    if (exp[0] == ',')
                        exp = exp[1..];
                }

                // Remove the last ')'
                leftOver = exp[1..];
                return new CompSize(arguments.ToArray());
            }
            else if (char.IsDigit(expression[0]))
            {
                int i = 1;
                while (i < expression.Length && char.IsDigit(expression[i]))
                {
                    i++;
                }

                leftOver = expression[i..];
                return new Constant(int.Parse(expression[0..i]));
            }
            else
            {
                if (char.IsLetter(expression[0]))
                {
                    int i = 1;
                    while (i < expression.Length && char.IsLetterOrDigit(expression[i]))
                    {
                        i++;
                    }

                    leftOver = expression[i..];
                    return new ParameterReference(expression[0..i]);
                }
                else throw new Exception($"Could not parse expression '{expression}'");
            }
        }

        private static PType2 ParsePType(XElement t)
        {
            var group = t.Attribute("group")?.Value;
            var length = t.Attribute("len")?.Value;

            var str = t.GetXmlText(element => !(element.Name == "name") ? element.Value : string.Empty).Trim();

            var pTypeLength = length == null ? null : ParseExpression(length);

            return new PType2(ParseType(str), group, pTypeLength);
        }

        private static GLType ParseType(string type)
        {
            type = type.Trim();

            if (type.EndsWith(']'))
            {
                int openingBracketIndex = type.LastIndexOf('[');
                if (openingBracketIndex != -1)
                {
                    var lengthStr = type.Substring(openingBracketIndex + 1, type.Length - openingBracketIndex - 2);
                    int length = int.Parse(lengthStr);

                    var baseType = ParseType(type.Substring(0, openingBracketIndex));

                    return new GLArrayType(baseType, false, length);
                }
                else
                {
                    throw new Exception("Missmatched brackets!");
                }
            }

            if (type.EndsWith('*'))
            {
                // We are parsing a pointer type.
                var withoutAsterisk = type[0..^1].TrimEnd();

                bool @const = false;
                if (withoutAsterisk.EndsWith("const"))
                {
                    @const = true;
                    withoutAsterisk = withoutAsterisk[0..^"const".Length];
                }
                else if (withoutAsterisk.StartsWith("const"))
                {
                    @const = true;
                    withoutAsterisk = withoutAsterisk["const".Length..];
                }

                var baseType = ParseType(withoutAsterisk);

                return new GLPointerType(baseType, @const);
            }

            // This is needed for _cl_context, and _cl_event
            // We don't care about struct here because it doesn't add any information (we are not a c compiler).
            if (type.StartsWith("struct"))
            {
                return ParseType(type["struct".Length..]);
            }

            // FIXME: Make this a 1 to 1 map
            PrimitiveType primitiveType = type switch
            {
                "void" => PrimitiveType.Void,
                "GLenum" => PrimitiveType.Enum,
                "GLboolean" => PrimitiveType.Byte,
                "GLbitfield" => PrimitiveType.Uint,
                "GLvoid" => PrimitiveType.Void,
                "GLbyte" => PrimitiveType.Sbyte,
                "GLubyte" => PrimitiveType.Byte,
                "GLshort" => PrimitiveType.Short,
                "GLushort" => PrimitiveType.Ushort,
                "GLint" => PrimitiveType.Int,
                "GLuint" => PrimitiveType.Uint,
                "GLclampx" => PrimitiveType.Int,
                "GLsizei" => PrimitiveType.Int,
                "GLfloat" => PrimitiveType.Float,
                "GLclampf" => PrimitiveType.Float,
                "GLdouble" => PrimitiveType.Double,
                "GLclampd" => PrimitiveType.Double,
                "GLeglClientBufferEXT" => PrimitiveType.VoidPtr,
                "GLeglImageOES" => PrimitiveType.VoidPtr,
                "GLchar" => PrimitiveType.Byte,
                "GLcharARB" => PrimitiveType.Byte,
                "GLhalf" => PrimitiveType.Short,
                "GLhalfARB" => PrimitiveType.Short,
                "GLfixed" => PrimitiveType.Int,
                "GLintptr" => PrimitiveType.IntPtr,
                "GLintptrARB" => PrimitiveType.IntPtr,
                "GLsizeiptr" => PrimitiveType.IntPtr,
                "GLsizeiptrARB" => PrimitiveType.IntPtr,
                "GLint64" => PrimitiveType.Int,
                "GLint64EXT" => PrimitiveType.Int,
                "GLuint64" => PrimitiveType.Uint,
                "GLuint64EXT" => PrimitiveType.Uint,
                "GLhalfNV" => PrimitiveType.Ushort,
                "GLvdpauSurfaceNV" => PrimitiveType.IntPtr,

                // FIXME
                "GLVULKANPROCNV" => PrimitiveType.Void,

                "GLhandleARB" => PrimitiveType.GLHandleARB, //This type is platform specific on apple.

                //The following have a custom c# implementation in the writer.
                "GLsync" => PrimitiveType.GLSync,
                "_cl_context" => PrimitiveType.CLContext,
                "_cl_event" => PrimitiveType.CLEvent,
                "GLDEBUGPROC" => PrimitiveType.GLDEBUGPROC,
                "GLDEBUGPROCARB" => PrimitiveType.GLDEBUGPROCARB,
                "GLDEBUGPROCKHR" => PrimitiveType.GLDEBUGPROCKHR,
                "GLDEBUGPROCAMD" => PrimitiveType.GLDEBUGPROCAMD,
                "GLDEBUGPROCNV" => PrimitiveType.GLDEBUGPROCNV,
                _ => PrimitiveType.Invalid,
            };

            if (primitiveType == PrimitiveType.Invalid)
            {
                Logger.Error($"Type conversion has not been created for type {type}");
                throw new Exception();
            }

            return new GLBaseType(type, primitiveType, false);
        }


        public static List<EnumsEntry> ParseEnums(XElement input)
        {
            Logger.Info("Begining parsing of enums.");
            List<EnumsEntry> enumsEntries = new List<EnumsEntry>();
            foreach (var enums in input.Elements("enums"))
            {
                var @namespace = enums.Attribute("namespace")?.Value;
                if (@namespace == null) throw new Exception($"Enums entry '{enums}' is missing a namespace attribute.");

                var group = enums.Attribute("group")?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries);

                var vendor = enums.Attribute("vendor")?.Value;

                var type = ParseEnumsType(enums.Attribute("type")?.Value);

                var startStr = enums.Attribute("start")?.Value;
                var endStr = enums.Attribute("end")?.Value;
                if ((startStr == null) != (endStr == null))
                    throw new Exception($"Enums entry '{enums}' is missing either a start or end attribute.");

                Range? range = null;
                if (startStr != null)
                {
                    var start = (int)new Int32Converter().ConvertFromString(startStr);
                    var end = (int)new Int32Converter().ConvertFromString(endStr);
                    range = new Range((Index)start, (Index)end);
                }

                var comment = enums.Attribute("comment")?.Value;

                List<EnumEntry2> entries = new List<EnumEntry2>();
                foreach (var @enum in enums.Elements("enum"))
                {
                    entries.Add(ParseEnumEntry(@enum));
                }

                enumsEntries.Add(new EnumsEntry(@namespace, group, type, vendor, range, comment, entries));
            }

            return enumsEntries;

            static EnumType ParseEnumsType(string? value) => value switch
            {
                null or "" => EnumType.None,
                "bitmask" => EnumType.Bitmask,
                _ => EnumType.Invalid,
            };
        }

        public static EnumEntry2 ParseEnumEntry(XElement @enum)
        {
            var name = @enum.Attribute("name")?.Value;
            var valueStr = @enum.Attribute("value")?.Value;
            if (valueStr == null || name == null)
            {
                throw new Exception($"Enum {name} did not pass correctly");
            }

            var suffix = ParseEnumTypeSuffix(@enum.Attribute("type")?.Value);

            var value = ConvertToUInt64(valueStr, suffix);

            var alias = @enum.Attribute("alias")?.Value;

            var groups = @enum.Attribute("group")?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var comment = @enum.Attribute("comment")?.Value;

            var api = Parser.ParseApi(@enum.Attribute("api")?.Value);

            return new EnumEntry2(name, api, value, alias, comment, groups, suffix);

            static TypeSuffix ParseEnumTypeSuffix(string? suffix) => suffix switch
            {
                null or "" => TypeSuffix.None,
                "u" => TypeSuffix.U,
                "ull" => TypeSuffix.Ull,
                _ => TypeSuffix.Invalid,
            };

            static ulong ConvertToUInt64(string val, TypeSuffix type) => type switch
            {
                TypeSuffix.None => (uint)(int)new Int32Converter().ConvertFromString(val),
                TypeSuffix.Ull => (ulong)(long)new Int64Converter().ConvertFromString(val),
                TypeSuffix.U => (ulong)(uint)new UInt32Converter().ConvertFromString(val),
                TypeSuffix.Invalid or _ => throw new Exception($"Invalid suffix '{type}'!"),
            };
        }


        public static List<Feature> ParseFeatures(XElement input, Dictionary<string, Command2> commands, List<EnumsEntry> enums)
        {
            Logger.Info("Begining parsing of features.");

            List<Feature> features = new List<Feature>();

            foreach (var feature in input.Elements("feature"))
            {
                var apiStr = feature.Attribute("api")?.Value;
                var name = feature.Attribute("name")?.Value;
                var number = feature.Attribute("number")?.Value;

                if (apiStr == null || name == null || number == null)
                {
                    throw new Exception($"Feature '{feature}' is missing an attribute.");
                }

                var version = Version.Parse(number);
                var api = ParseApi(apiStr);

                List<RequireEntry> requireEntries = new List<RequireEntry>();
                foreach (var require in feature.Elements("require"))
                {
                    RequireEntry reqEntry = ParseRequire(require, commands, enums);
                    requireEntries.Add(reqEntry);
                }

                List<RemoveEntry> removeEntries = new List<RemoveEntry>();
                foreach (var remove in feature.Elements("remove"))
                {
                    RemoveEntry removeEntry = ParseRemove(remove, commands, enums);
                    removeEntries.Add(removeEntry);
                }

                features.Add(new Feature(api, version, name, requireEntries, removeEntries));
            }

            return features;
        }

        public static List<Extension> ParseExtensions(XElement input, Dictionary<string, Command2> commands, List<EnumsEntry> enums)
        {
            List<Extension> extensions = new List<Extension>();

            foreach (var extension in input.Elements("extension"))
            {
                var extName = extension.Attribute("name")?.Value;
                if (extName == null)
                {
                    throw new Exception($"Extension '{extension}' is missing a name!");
                }

                var vendor = extName[3..extName.IndexOf("_", 3, StringComparison.Ordinal)];
                if (string.IsNullOrEmpty(vendor))
                {
                    throw new Exception($"Extension '{extension}' doesn't have the vendor in it's name!");
                }

                var comment = extension.Attribute("comment")?.Value;

                var supportedApis = extension
                    .Attribute("supported")?.Value?
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => ParseApi(s)).ToArray();

                if (supportedApis == null)
                {
                    throw new Exception($"Extension '{extension}' did not specify any supported APIs.");
                }

                List<RequireEntry> requires = new List<RequireEntry>();
                foreach (var require in extension.Elements("require"))
                {
                    requires.Add(Parser.ParseRequire(extension, commands, enums));
                }

                extensions.Add(new Extension(extName, vendor, supportedApis, comment, requires));
            }

            return extensions;
        }

        public static RequireEntry ParseRequire(XElement requires, Dictionary<string, Command2> commands, List<EnumsEntry> enums)
        {
            var api = ParseApi(requires.Attribute("api")?.Value);
            var profile = ParseProfile(requires.Attribute("profile")?.Value);
            var comment = requires.Attribute("comment")?.Value;

            List<Command2> reqCommands = new List<Command2>();
            List<EnumEntry2> reqEnums = new List<EnumEntry2>();

            foreach (var entry in requires.Elements())
            {
                string? name = entry.Attribute("name")?.Value;
                if (name == null) throw new Exception($"The entry '{entry}' didn't contain a name attribute.");

                switch (entry.Name.LocalName)
                {
                    case "command":
                        if (commands.TryGetValue(name, out var command))
                        {
                            reqCommands.Add(command);
                        }
                        else
                        {
                            throw new Exception($"No command called '{name}' found!");
                        }
                        break;
                    case "enum":
                        bool found = false;
                        foreach (var e in enums)
                        {
                            foreach (var @enum in e.Enums)
                            {
                                if (@enum.Name == name)
                                {
                                    reqEnums.Add(@enum);
                                    found = true;
                                    break;
                                }
                            }

                            if (found) break;
                        }
                        if (found) break;

                        throw new Exception("This enum is required, but it doesn't exist in the first place...");
                    default:
                        continue;
                }
            }

            return new RequireEntry(api, profile, comment, reqCommands, reqEnums);
        }

        public static RemoveEntry ParseRemove(XElement requires, Dictionary<string, Command2> commands, List<EnumsEntry> enums)
        {
            var profile = ParseProfile(requires.Attribute("profile")?.Value);
            var comment = requires.Attribute("comment")?.Value;

            List<Command2> removeCommands = new List<Command2>();
            List<EnumEntry2> removeEnums = new List<EnumEntry2>();

            foreach (var entry in requires.Elements())
            {
                string? name = entry.Attribute("name")?.Value;
                if (name == null) throw new Exception($"The entry '{entry}' didn't contain a name attribute.");

                switch (entry.Name.LocalName)
                {
                    case "command":
                        if (commands.TryGetValue(name, out var command))
                        {
                            removeCommands.Add(command);
                        }
                        else
                        {
                            throw new Exception($"No command called '{name}' found!");
                        }
                        break;
                    case "enum":
                        bool found = false;
                        foreach (var e in enums)
                        {
                            foreach (var @enum in e.Enums)
                            {
                                if (@enum.Name == name)
                                {
                                    removeEnums.Add(@enum);
                                    found = true;
                                    break;
                                }
                            }

                            if (found) break;
                        }
                        if (found) break;

                        throw new Exception("This enum was removed, but it didn't exist in the first place...");
                    default:
                        continue;
                }
            }

            return new RemoveEntry(profile, comment, removeCommands, removeEnums);
        }



        public static GLAPI ParseApi(string? api) => api switch
        {
            null or "" => GLAPI.None,

            "gl" => GLAPI.GL,
            "gles1" => GLAPI.GLES1,
            "gles2" => GLAPI.GLES2,
            "glsc2" => GLAPI.GLSC2,

            _ => GLAPI.Invalid,
        };

        public static GLProfile ParseProfile(string? profile) => profile switch
        {
            null or "" => GLProfile.None,

            "core" => GLProfile.Core,
            "compatibility" => GLProfile.Compatibility,
            "common" => GLProfile.Common,

            _ => GLProfile.Invalid,
        };
    }
}
