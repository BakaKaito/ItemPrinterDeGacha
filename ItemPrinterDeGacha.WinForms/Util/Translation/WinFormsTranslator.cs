using System.Text;

namespace ItemPrinterDeGacha.WinForms;

public static class WinFormsTranslator
{
    private static readonly Dictionary<string, TranslationContext> Context = [];
    internal static void TranslateInterface(this Control form, string lang) => TranslateForm(form, GetContext(lang));

    private static string GetTranslationFileNameInternal(ReadOnlySpan<char> lang) => $"lang_{lang}";
    private static string GetTranslationFileNameExternal(ReadOnlySpan<char> lang) => $"lang_{lang}.txt";

    public static IReadOnlyDictionary<string, string> GetDictionary(string lang) => GetContext(lang).Lookup;

    private static TranslationContext GetContext(string lang)
    {
        if (Context.TryGetValue(lang, out var context))
            return context;

        var lines = GetTranslationFile(lang);
        Context.Add(lang, context = new TranslationContext(lines));
        return context;
    }

    private static void TranslateForm(Control form, TranslationContext context)
    {
        form.SuspendLayout();

        // Translate Title
        var formName = form.Name;
        formName = GetSaneFormName(formName);
        form.Text = context.GetTranslatedText(formName, form.Text);

        // Translate Controls
        var translatable = GetTranslatableControls(form);
        foreach (var c in translatable)
            TranslateControl(c, context, formName);

        form.ResumeLayout();
    }

    internal static void TranslateControls(IEnumerable<Control> controls)
    {
        foreach (var c in controls)
        {
            foreach (var context in Context.Values)
                context.GetTranslatedText(c.Name, c.Text);
        }
    }

    private static string GetSaneFormName(string formName)
    {
        // Strip out generic form names
        var degen = formName.IndexOf('`');
        if (degen != -1)
            formName = formName[..degen];

        return formName switch
        {
            _ => formName,
        };
    }

    private static void TranslateControl(object c, TranslationContext context, ReadOnlySpan<char> formname)
    {
        if (c is Control r)
        {
            var current = r.Text;
            var updated = context.GetTranslatedText($"{formname}.{r.Name}", current);
            if (!ReferenceEquals(current, updated))
                r.Text = updated;
        }
        else if (c is ToolStripItem t)
        {
            var current = t.Text;
            var updated = context.GetTranslatedText($"{formname}.{t.Name}", current);
            if (!ReferenceEquals(current, updated))
                t.Text = updated;
        }
    }

    private static ReadOnlySpan<string> GetTranslationFile(ReadOnlySpan<char> lang)
    {
        var file = GetTranslationFileNameInternal(lang);
        // Check to see if the desired translation file exists in the same folder as the executable
        string externalLangPath = GetTranslationFileNameExternal(file);
        if (File.Exists(externalLangPath))
        {
            try { return File.ReadAllLines(externalLangPath); }
            catch { /* In use? Just return the internal resource. */ }
        }
        return ResourceUtil.GetStringList(file);
    }

    private static IEnumerable<object> GetTranslatableControls(Control f)
    {
        foreach (var z in f.GetChildrenOfType<Control>())
        {
            switch (z)
            {
                case ToolStrip menu:
                    foreach (var obj in GetToolStripMenuItems(menu))
                        yield return obj;

                    break;
                default:
                    if (string.IsNullOrWhiteSpace(z.Name))
                        break;

                    if (z.ContextMenuStrip != null) // control has attached MenuStrip
                    {
                        foreach (var obj in GetToolStripMenuItems(z.ContextMenuStrip))
                            yield return obj;
                    }

                    if (z is ListControl or TextBoxBase or LinkLabel or NumericUpDown or ContainerControl)
                        break; // undesirable to modify, ignore

                    if (!string.IsNullOrWhiteSpace(z.Text))
                        yield return z;
                    break;
            }
        }
    }

    private static IEnumerable<T> GetChildrenOfType<T>(this Control control) where T : class
    {
        foreach (var child in control.Controls.OfType<Control>())
        {
            if (child is T childOfT)
                yield return childOfT;

            if (!child.HasChildren) continue;
            foreach (var descendant in GetChildrenOfType<T>(child))
                yield return descendant;
        }
    }

    private static IEnumerable<object> GetToolStripMenuItems(ToolStrip menu)
    {
        foreach (var i in menu.Items.OfType<ToolStripMenuItem>())
        {
            if (!string.IsNullOrWhiteSpace(i.Text))
                yield return i;
            foreach (var sub in GetToolsStripDropDownItems(i).Where(z => !string.IsNullOrWhiteSpace(z.Text)))
                yield return sub;
        }
    }

    private static IEnumerable<ToolStripMenuItem> GetToolsStripDropDownItems(ToolStripDropDownItem item)
    {
        foreach (var dropDownItem in item.DropDownItems.OfType<ToolStripMenuItem>())
        {
            yield return dropDownItem;
            if (!dropDownItem.HasDropDownItems)
                continue;
            foreach (ToolStripMenuItem subItem in GetToolsStripDropDownItems(dropDownItem))
                yield return subItem;
        }
    }

#if DEBUG
    public static void UpdateAll(string baseLanguage, IEnumerable<string> others)
    {
        var baseContext = GetContext(baseLanguage);
        foreach (var lang in others)
        {
            var c = GetContext(lang);
            c.UpdateFrom(baseContext);
        }
    }

    public static void DumpAll(ReadOnlySpan<string> banlist)
    {
        foreach (var (lang, value) in Context)
        {
            var fn = GetTranslationFileNameExternal(lang);
            var lines = value.Write();

            // Write a new file.
            using var fs = new StreamWriter(fn);
            foreach (var line in lines)
            {
                // Ensure line isn't banned.
                if (IsBannedContains(line, banlist))
                    continue;
                fs.WriteLine(line);
            }
        }
    }

    private static bool IsBannedContains(ReadOnlySpan<char> line, ReadOnlySpan<string> banlist)
    {
        foreach (var banned in banlist)
        {
            if (banned.AsSpan().Contains(line, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsBannedStartsWith(ReadOnlySpan<char> line, ReadOnlySpan<string> banlist)
    {
        foreach (var banned in banlist)
        {
            if (line.StartsWith(banned, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static void LoadAllForms(IEnumerable<Type> types, ReadOnlySpan<string> banlist)
    {
        foreach (var t in types)
        {
            if (t.BaseType != typeof(Form) || IsBannedStartsWith(t.Name, banlist))
                continue;

            var constructors = t.GetConstructors();
            if (constructors.Length == 0)
            { System.Diagnostics.Debug.WriteLine($"No constructors: {t.Name}"); continue; }
            var argCount = constructors[0].GetParameters().Length;
            try
            {
                _ = (Form?)Activator.CreateInstance(t, new object[argCount]);
            }
            // This is a debug utility method, will always be logging. Shouldn't ever fail.
            catch
            {
                System.Diagnostics.Debug.Write($"Failed to create a new form {t}");
            }
        }
    }

    public static void SetRemovalMode(bool status = true)
    {
        foreach (TranslationContext c in Context.Values)
        {
            c.RemoveUsedKeys = status;
            c.AddNew = !status;
        }
    }

    public static void RemoveAll(string defaultLanguage, ReadOnlySpan<string> banlist)
    {
        var badKeys = Context[defaultLanguage];
        var split = GetSkips(banlist, badKeys);
        foreach (var c in Context)
        {
            var lang = c.Key;
            var fn = GetTranslationFileNameExternal(lang);
            var lines = File.ReadAllLines(fn);
            var result = lines.Where(l => !split.Any(s => l.StartsWith(s + TranslationContext.Separator)));
            File.WriteAllLines(fn, result, Encoding.UTF8);
        }
    }

    private static string[] GetSkips(ReadOnlySpan<string> banlist, TranslationContext badKeys)
    {
        List<string> split = [];
        foreach (var line in badKeys.Write())
        {
            var index = line.IndexOf(TranslationContext.Separator);
            if (index < 0)
                continue;
            var key = line.AsSpan(0, index);
            if (IsBannedStartsWith(key, banlist))
                split.Add(key.ToString());
        }

        if (split.Count == 0)
            return [];
        return [..split];
    }
#endif
}

public sealed class TranslationContext
{
    public bool AddNew { private get; set; }
    public bool RemoveUsedKeys { private get; set; }
    public const char Separator = '=';
    private readonly Dictionary<string, string> Translation = [];
    public IReadOnlyDictionary<string, string> Lookup => Translation;

    public TranslationContext(ReadOnlySpan<string> content, char separator = Separator)
    {
        foreach (var line in content)
            LoadLine(line, separator);
    }

    private void LoadLine(ReadOnlySpan<char> line, char separator = Separator)
    {
        var split = line.IndexOf(separator);
        if (split < 0)
            return; // ignore
        var key = line[..split].ToString();
        var value = line[(split + 1)..].ToString();
        Translation.TryAdd(key, value);
    }

    public string? GetTranslatedText(string val, string? fallback)
    {
        if (RemoveUsedKeys)
            Translation.Remove(val);

        if (Translation.TryGetValue(val, out var translated))
            return translated;

        if (fallback != null && AddNew)
            Translation.Add(val, fallback);
        return fallback;
    }

    public IEnumerable<string> Write(char separator = Separator)
    {
        return Translation.Select(z => $"{z.Key}{separator}{z.Value}").OrderBy(z => z.Contains('.')).ThenBy(z => z);
    }

    public void UpdateFrom(TranslationContext other)
    {
        bool oldAdd = AddNew;
        AddNew = true;
        foreach (var kvp in other.Translation)
            GetTranslatedText(kvp.Key, kvp.Value);
        AddNew = oldAdd;
    }
}
