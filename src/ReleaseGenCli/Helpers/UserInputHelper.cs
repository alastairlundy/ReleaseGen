namespace ReleaseGenCli.Helpers;

public static class UserInputHelper
{
    
    public static string GetRuntimeIdentifier()
    {
        string[] choices =
        [
            "win-x64",
            "win-arm64",
            "linux-x64",
            "linux-arm",
            "linux-arm64",
            "osx-x64",
            "osx-arm64"
        ];
        
        string output = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(
                    Resources.Prompts_Selection_StorageDrive.Replace(
                        Resources.Prompts_Highlights_StorageDrive,
                        $"[underline italic gold1]{Resources.Prompts_Highlights_StorageDrive}"
                    )
                )
                .PageSize(10)
                .AddChoices(choices)
                .MoreChoicesText($"({Resources.Prompts_Selection_RevealMoreOptions})")
        );

        return output;
    }
}