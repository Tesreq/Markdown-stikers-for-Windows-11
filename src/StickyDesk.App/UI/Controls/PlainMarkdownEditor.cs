namespace StickyDesk;

internal sealed class PlainMarkdownEditor : RichTextBox
{
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (keys == (Keys.Control | Keys.V) || keys == (Keys.Shift | Keys.Insert))
        {
            try { if (Clipboard.ContainsText()) SelectedText = Clipboard.GetText(TextDataFormat.UnicodeText); }
            catch (System.Runtime.InteropServices.ExternalException) { System.Media.SystemSounds.Beep.Play(); }
            return true;
        }
        if (keys == (Keys.Control | Keys.Y)) { if (CanRedo) Redo(); return true; }
        if (keys == (Keys.Control | Keys.A)) { SelectAll(); return true; }
        return base.ProcessCmdKey(ref message, keys);
    }
}
