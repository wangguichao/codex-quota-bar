using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexQuotaBar;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Drawing.Icon image;
    private readonly Forms.ToolStripMenuItem visibility, positionLock;
    private readonly Action restore;
    internal Forms.ContextMenuStrip Menu { get; } = new();
    internal bool IsDisposed { get; private set; }
    internal bool Visible => !IsDisposed && icon.Visible;
    internal TrayController(Action restore, Action toggleVisibility, Action toggleLock, Action reset, Action exit)
    {
        this.restore = restore;
        image = CreateIcon();
        Forms.ToolStripMenuItem Item(string name, string text, Action action)
        {
            var item = new Forms.ToolStripMenuItem(text) { Name = name };
            item.Click += (_, _) => { if (!IsDisposed) action(); }; Menu.Items.Add(item); return item;
        }
        Item("restore", "打开设置", Restore);
        visibility = Item("visibility", "隐藏横条", toggleVisibility);
        positionLock = Item("lock", "锁定位置（鼠标穿透）", toggleLock);
        Item("reset", "重置横条位置", reset);
        Menu.Items.Add(new Forms.ToolStripSeparator());
        Item("exit", "退出", exit);
        icon = new Forms.NotifyIcon { Icon = image, ContextMenuStrip = Menu, Text = "Codex额度条 · 双击打开设置" };
        icon.DoubleClick += (_, _) => Restore();
        try { icon.Visible = true; }
        catch { icon.Dispose(); Menu.Dispose(); image.Dispose(); throw; }
    }
    internal void Restore() { if (!IsDisposed) restore(); }
    internal void Update(bool showing, bool locked, string status)
    {
        if (IsDisposed) return;
        visibility.Text = showing ? "隐藏横条" : "显示横条";
        positionLock.Checked = locked;
        var text = "Codex额度条 · " + status;
        icon.Text = text.Length > 60 ? text[..60] : text;
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true; icon.Visible = false; icon.Dispose(); Menu.Dispose(); image.Dispose();
    }
    private static Drawing.Icon CreateIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        using (var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(38, 108, 89)))
        using (var font = new Drawing.Font("Segoe UI", 21, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Drawing.Color.Transparent);
            graphics.FillEllipse(background, 1, 1, 30, 30);
            graphics.DrawString("Q", font, Drawing.Brushes.White, 6, 2);
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Drawing.Icon.FromHandle(handle); return (Drawing.Icon)borrowed.Clone(); }
        finally { NativeWindows.DestroyIcon(handle); }
    }
}

