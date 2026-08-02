using System.Drawing;
using System.Runtime.InteropServices;
using AudioShare.Core;
using Forms = System.Windows.Forms;

namespace AudioShare.App;

internal sealed class FlowCastTrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ToolStripMenuItem startItem;
    private readonly Forms.ToolStripMenuItem muteItem;
    private readonly Forms.ToolStripMenuItem stopItem;
    private Icon? currentIcon;
    private IntPtr currentIconHandle;

    public FlowCastTrayIcon(Action showWindow, Action startSharing, Action toggleMute, Action stopSharing, Action exitApplication)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 FlowCast", null, (_, _) => showWindow());
        startItem = new Forms.ToolStripMenuItem("开始分享", null, (_, _) => startSharing());
        muteItem = new Forms.ToolStripMenuItem("静音分享", null, (_, _) => toggleMute());
        stopItem = new Forms.ToolStripMenuItem("停止分享", null, (_, _) => stopSharing());
        menu.Items.Add(startItem);
        menu.Items.Add(muteItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("结束 FlowCast", null, (_, _) => exitApplication());

        notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "FlowCast - 只自己听到",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => showWindow();
        SetState(ShareSessionState.Idle);
    }

    public void SetState(ShareSessionState state)
    {
        var (text, color) = state switch
        {
            ShareSessionState.Sharing => ("FlowCast - 正在分享", Color.MediumSeaGreen),
            ShareSessionState.Muted => ("FlowCast - 分享已静音", Color.DarkOrange),
            ShareSessionState.Disconnected => ("FlowCast - 分享已断开", Color.IndianRed),
            _ => ("FlowCast - 只自己听到", Color.SlateGray),
        };

        notifyIcon.Text = text;
        startItem.Enabled = state is ShareSessionState.Idle or ShareSessionState.Disconnected;
        muteItem.Enabled = state is ShareSessionState.Sharing or ShareSessionState.Muted;
        muteItem.Text = state == ShareSessionState.Muted ? "恢复分享" : "静音分享";
        stopItem.Enabled = state is ShareSessionState.Sharing or ShareSessionState.Muted or ShareSessionState.Disconnected;
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
