using System.Drawing;
using System.Runtime.InteropServices;
using AudioShare.Core;
using Forms = System.Windows.Forms;

namespace AudioShare.App;

/// <summary>A shareable program shown in the tray quick-switch submenu.</summary>
internal sealed record TrayProgramEntry(string DisplayName, bool IsShared, Action Share);

internal sealed class FlowCastTrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ToolStripMenuItem startItem;
    private readonly Forms.ToolStripMenuItem stopItem;
    private readonly Forms.ToolStripMenuItem programsItem;
    private readonly Func<IReadOnlyList<TrayProgramEntry>> getPrograms;
    private Icon? currentIcon;
    private IntPtr currentIconHandle;

    public FlowCastTrayIcon(
        Action showWindow,
        Action startSharing,
        Action stopSharing,
        Action exitApplication,
        Func<IReadOnlyList<TrayProgramEntry>> getPrograms)
    {
        this.getPrograms = getPrograms ?? throw new ArgumentNullException(nameof(getPrograms));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打開 FlowCast", null, (_, _) => showWindow());
        startItem = new Forms.ToolStripMenuItem("開始分享", null, (_, _) => startSharing());
        stopItem = new Forms.ToolStripMenuItem("停止分享", null, (_, _) => stopSharing());
        programsItem = new Forms.ToolStripMenuItem("切換分享的程序");
        menu.Items.Add(startItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(programsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("結束 FlowCast", null, (_, _) => exitApplication());
        menu.Opening += (_, _) => RebuildProgramsMenu();

        notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "FlowCast - 只自己聽到",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => showWindow();
        SetState(ShareSessionState.Idle);
    }

    private void RebuildProgramsMenu()
    {
        programsItem.DropDownItems.Clear();
        var programs = getPrograms();
        if (programs.Count == 0)
        {
            programsItem.DropDownItems.Add(new Forms.ToolStripMenuItem("暫無可分享的程序") { Enabled = false });
            return;
        }

        foreach (var program in programs)
        {
            // Already-shared program is shown checked and disabled; picking another routes
            // through the same confirm/switch path the main window uses.
            var share = program.Share;
            var item = new Forms.ToolStripMenuItem(program.DisplayName, null, (_, _) => share())
            {
                Checked = program.IsShared,
                Enabled = !program.IsShared,
            };
            programsItem.DropDownItems.Add(item);
        }
    }

    public void SetState(ShareSessionState state)
    {
        var (text, color) = state switch
        {
            ShareSessionState.Sharing => ("FlowCast - 正在分享", Color.MediumSeaGreen),
            ShareSessionState.Disconnected => ("FlowCast - 分享已斷開", Color.IndianRed),
            _ => ("FlowCast - 只自己聽到", Color.SlateGray),
        };

        notifyIcon.Text = text;
        startItem.Enabled = state is ShareSessionState.Idle or ShareSessionState.Disconnected;
        stopItem.Enabled = state is ShareSessionState.Sharing or ShareSessionState.Disconnected;
        ReplaceIcon(color);
    }

    public void ShowDisconnect(string reason) =>
        notifyIcon.ShowBalloonTip(4000, "FlowCast 已停止分享", reason, Forms.ToolTipIcon.Warning);

    private void ReplaceIcon(Color color)
    {
        IntPtr handle;
        using (var bitmap = new Bitmap(32, 32))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(color))
        {
            graphics.Clear(Color.Transparent);
            graphics.FillEllipse(brush, 3, 3, 26, 26);
            graphics.FillEllipse(Brushes.White, 11, 8, 10, 10);
            handle = bitmap.GetHicon();
        }

        var newIcon = Icon.FromHandle(handle);
        var previousIcon = currentIcon;
        var previousHandle = currentIconHandle;
        currentIcon = newIcon;
        currentIconHandle = handle;
        notifyIcon.Icon = newIcon;
        if (previousIcon is not null)
        {
            previousIcon.Dispose();
            DestroyIcon(previousHandle);
        }
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        if (currentIcon is not null)
        {
            currentIcon.Dispose();
            DestroyIcon(currentIconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
