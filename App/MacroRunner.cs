namespace FingerprintMacroPad;

/// <summary>Executes a configured <see cref="MacroAction"/>. Safe to call off the UI thread.</summary>
public static class MacroRunner
{
    public static void Run(MacroAction? action)
    {
        if (action == null) return;
        try
        {
            switch (action.Type)
            {
                case ActionType.None:
                    break;
                case ActionType.LaunchApp:
                    ProcessLauncher.Launch(action.Target, action.Arguments);
                    break;
                case ActionType.OpenPath:
                    ProcessLauncher.Open(action.Target);
                    break;
                case ActionType.RunScript:
                    ProcessLauncher.RunScript(action.Target, action.Arguments);
                    break;
                case ActionType.InlineScript:
                    ScriptRunner.RunInline(action.ScriptBody, action.ScriptLang);
                    break;
                case ActionType.Hotkey:
                    KeySender.SendHotkey(action.Hotkey);
                    break;
                case ActionType.MediaKey:
                    KeySender.SendMedia(action.Media);
                    break;
            }
        }
        catch { /* never let a bad macro crash the engine */ }
    }
}
