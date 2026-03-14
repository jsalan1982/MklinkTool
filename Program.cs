using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace MklinkTool
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private TextBox txtSource;
        private TextBox txtTarget;
        private CheckBox chkMove;
        private RadioButton radJunction;
        private RadioButton radSymlink;
        private RadioButton radHardlink;
        private RichTextBox txtLog;
        private Button btnRun;

        public MainForm()
        {
            this.Text = "Mklink 增强版 (C# 原生极速版)";
            this.Size = new Size(800, 600);
            this.BackColor = Color.FromArgb(14, 17, 22);
            this.ForeColor = Color.FromArgb(230, 237, 243);

            SetupUI();
        }

        private void SetupUI()
        {
            int y = 20;

            // 1. 类型选择
            Label lblType = new Label { Text = "1. 选择链接类型:", Location = new Point(20, y), AutoSize = true };
            this.Controls.Add(lblType); y += 25;

            radJunction = new RadioButton { Text = "/J 目录联接 (推荐)", Location = new Point(30, y), Checked = true, AutoSize = true };
            radSymlink = new RadioButton { Text = "/D 符号链接", Location = new Point(200, y), AutoSize = true };
            radHardlink = new RadioButton { Text = "/H 硬链接", Location = new Point(350, y), AutoSize = true };
            this.Controls.Add(radJunction); this.Controls.Add(radSymlink); this.Controls.Add(radHardlink);
            y += 40;

            // 2. 模式选择
            Label lblMode = new Label { Text = "2. 核心模式:", Location = new Point(20, y), AutoSize = true };
            this.Controls.Add(lblMode); y += 25;

            chkMove = new CheckBox { Text = "移动内容 (搬家模式)", Location = new Point(30, y), AutoSize = true };
            this.Controls.Add(chkMove); y += 40;

            // 3. 路径输入 (支持拖拽)
            Label lblSrc = new Label { Text = "源路径 (拖拽文件/文件夹到此):", Location = new Point(20, y), AutoSize = true };
            this.Controls.Add(lblSrc); y += 25;
            txtSource = CreateDragDropTextBox(y);
            this.Controls.Add(txtSource); y += 40;

            Label lblTgt = new Label { Text = "目标路径 (拖拽文件夹到此):", Location = new Point(20, y), AutoSize = true };
            this.Controls.Add(lblTgt); y += 25;
            txtTarget = CreateDragDropTextBox(y);
            this.Controls.Add(txtTarget); y += 50;

            // 4. 执行按钮
            btnRun = new Button { Text = "🚀 开始执行", Location = new Point(20, y), Size = new Size(740, 40), BackColor = Color.FromArgb(47, 111, 237), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnRun.Click += BtnRun_Click;
            this.Controls.Add(btnRun); y += 60;

            // 5. 日志区
            txtLog = new RichTextBox { Location = new Point(20, y), Size = new Size(740, 200), BackColor = Color.FromArgb(11, 15, 22), ForeColor = Color.LightGreen, ReadOnly = true };
            this.Controls.Add(txtLog);
        }

        private TextBox CreateDragDropTextBox(int y)
        {
            TextBox txt = new TextBox { Location = new Point(20, y), Size = new Size(740, 25), BackColor = Color.FromArgb(15, 20, 27), ForeColor = Color.White };
            txt.AllowDrop = true;
            txt.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            txt.DragDrop += (s, e) => {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0) ((TextBox)s).Text = files[0];
            };
            return txt;
        }

        private void Log(string msg)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Log), msg); return; }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            txtLog.ScrollToCaret();
        }

        private async void BtnRun_Click(object sender, EventArgs e)
        {
            string src = txtSource.Text.Trim();
            string tgt = txtTarget.Text.Trim();

            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(tgt)) { MessageBox.Show("路径不能为空！"); return; }

            btnRun.Enabled = false;
            Log("=== 任务开始 ===");

            await Task.Run(() =>
            {
                string finalSource = src;
                string finalTargetDir = tgt;
                string basename = Path.GetFileName(src);
                string linkPath = Path.Combine(finalTargetDir, basename);

                try
                {
                    if (!Directory.Exists(finalTargetDir)) Directory.CreateDirectory(finalTargetDir);

                    // 如果勾选了搬家，先移动文件
                    if (chkMove.Checked)
                    {
                        Log($"📦 正在移动: {src} -> {linkPath}");
                        if (File.Exists(src)) File.Move(src, linkPath);
                        else if (Directory.Exists(src)) Directory.Move(src, linkPath);
                        else throw new Exception("源路径不存在。");
                        
                        // 移动后，需要在原位置建立链接指向新位置
                        string temp = src;
                        src = linkPath;
                        linkPath = temp;
                    }

                    // 确定参数
                    string linkType = radJunction.Checked ? "/J" : (radSymlink.Checked ? "/D" : "/H");
                    if (File.Exists(src) && linkType != "/H") linkType = ""; // 文件默认不带参数或带硬链接参数

                    // 执行 mklink
                    Log($"🔗 正在建立链接...");
                    string cmdArgs = $"/c mklink {linkType} \"{linkPath}\" \"{src}\"";
                    
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmdArgs)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process p = Process.Start(psi))
                    {
                        p.WaitForExit();
                        if (p.ExitCode == 0) Log("✅ 链接创建成功！");
                        else Log($"❌ 失败: {p.StandardError.ReadToEnd()}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"❌ 发生异常: {ex.Message}");
                }
            });

            Log("=== 任务结束 ===");
            btnRun.Enabled = true;
        }
    }
}
