using System.Windows;

namespace FingerprintMacroPad;

internal static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.OriginalString ?? "";
            if (src.Contains("Themes/Dark.xaml") || src.Contains("Themes/Light.xaml"))
                dicts.RemoveAt(i);
        }
        string name = theme == AppTheme.Light ? "Light" : "Dark";
        dicts.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/FingerprintMacroPad;component/Themes/{name}.xaml")
        });
    }
}
