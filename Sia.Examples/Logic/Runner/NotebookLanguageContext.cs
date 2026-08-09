namespace Sia_Examples.Notebook;

public static class NotebookLanguageContext
{
    public const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Sia;
        """;

    public static string GetGlobalUsings(bool includeWrapperNamespace)
        => includeWrapperNamespace
            ? $"{GlobalUsings}\nglobal using {NotebookProgramBuilder.WrapperNamespace};"
            : GlobalUsings;
}
