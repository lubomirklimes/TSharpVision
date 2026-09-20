using System.Reflection;
using TSharpVision.CodeEditor.Syntax;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

/// <summary>What the reusable extension may depend on, and what its public surface may show.</summary>
public sealed class BoundaryTests
{
    private static readonly Assembly Extension = typeof(EditorSyntaxHighlighter).Assembly;

    [Fact]
    public void TheExtensionReferencesNoCommanderAssembly()
    {
        string[] references = Extension.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("TSharpCommander", StringComparison.Ordinal));
        Assert.Contains("TSharpVision", references);
        Assert.All(references, name => Assert.True(
            name is "TSharpVision" or "TextMateSharp" or "TextMateSharp.Grammars" or "netstandard"
            || name.StartsWith("System", StringComparison.Ordinal),
            $"unexpected reference {name}"));
    }

    [Fact]
    public void NoPublicSignatureExposesATextMateSharpType()
    {
        var offending = new List<string>();

        foreach (Type type in Extension.GetExportedTypes())
        {
            Check(type.BaseType, $"{type} base");
            foreach (Type contract in type.GetInterfaces()) Check(contract, $"{type} implements");

            const BindingFlags visible = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                         | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (MemberInfo member in type.GetMembers(visible))
            {
                switch (member)
                {
                    case FieldInfo field when field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly:
                        Check(field.FieldType, $"{type}.{field.Name}");
                        break;
                    case PropertyInfo property when property.GetMethod is { } getter && (getter.IsPublic || getter.IsFamily):
                        Check(property.PropertyType, $"{type}.{property.Name}");
                        break;
                    case MethodBase method when method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly:
                        if (method is MethodInfo info) Check(info.ReturnType, $"{type}.{method.Name} returns");
                        foreach (ParameterInfo parameter in method.GetParameters())
                            Check(parameter.ParameterType, $"{type}.{method.Name}({parameter.Name})");
                        break;
                }
            }
        }

        Assert.Empty(offending);

        void Check(Type? candidate, string where)
        {
            if (candidate is null) return;
            if (candidate.HasElementType) { Check(candidate.GetElementType(), where); return; }
            if (candidate.IsGenericType) foreach (Type argument in candidate.GetGenericArguments()) Check(argument, where);

            string? assembly = candidate.Assembly.GetName().Name;
            if (assembly is not null && (assembly.StartsWith("TextMateSharp", StringComparison.Ordinal) || assembly.StartsWith("Onigwrap", StringComparison.Ordinal)))
                offending.Add($"{where}: {candidate.FullName}");
        }
    }
}
