#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")] // 隐藏运行时的黑框控制台

use eframe::egui;
use std::fs;
use std::os::windows::process::CommandExt;
use std::path::Path;
use std::process::Command;
use std::sync::mpsc::{self, Receiver, Sender};
use std::thread;

const CREATE_NO_WINDOW: u32 = 0x08000000;

fn main() -> eframe::Result<()> {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([700.0, 550.0])
            .with_title("Mklink 增强版 (Rust 极致轻量版)"),
        ..Default::default()
    };
    eframe::run_native(
        "MklinkTool",
        options,
        Box::new(|_cc| Box::new(MyApp::default())),
    )
}

struct MyApp {
    source: String,
    target: String,
    link_type: String,
    is_move: bool,
    overwrite: bool,
    logs: String,
    log_rx: Receiver<String>,
    log_tx: Sender<String>,
    is_running: bool,
}

impl Default for MyApp {
    fn default() -> Self {
        let (tx, rx) = mpsc::channel();
        Self {
            source: String::new(),
            target: String::new(),
            link_type: "/J".to_string(),
            is_move: false,
            overwrite: false,
            logs: "=== Rust 极速核心已就绪 ===\n".to_string(),
            log_rx: rx,
            log_tx: tx,
            is_running: false,
        }
    }
}

impl eframe::App for MyApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        // 接收后台日志并更新 UI
        while let Ok(msg) = self.log_rx.try_recv() {
            if msg == "DONE" {
                self.is_running = false;
            } else {
                self.logs.push_str(&format!("{}\n", msg));
            }
        }

        // 处理拖拽文件
        ctx.input(|i| {
            if !i.raw.dropped_files.is_empty() {
                if let Some(path) = &i.raw.dropped_files[0].path {
                    let path_str = path.display().to_string();
                    if self.source.is_empty() {
                        self.source = path_str;
                    } else {
                        self.target = path_str;
                    }
                }
            }
        });

        egui::CentralPanel::default().show(ctx, |ui| {
            ui.heading("Mklink 极速构建工具");
            ui.separator();

            // 1. 类型选择
            ui.label("1. 链接类型:");
            ui.horizontal(|ui| {
                ui.radio_value(&mut self.link_type, "/D".to_string(), "/D 符号链接");
                ui.radio_value(&mut self.link_type, "/J".to_string(), "/J 目录联接(推荐)");
                ui.radio_value(&mut self.link_type, "/H".to_string(), "/H 硬链接");
            });
            ui.add_space(10.0);

            // 2. 模式选择
            ui.label("2. 核心模式:");
            ui.horizontal(|ui| {
                ui.checkbox(&mut self.is_move, "移动内容 (搬家模式)");
                ui.checkbox(&mut self.overwrite, "移动时覆盖旧文件");
            });
            ui.add_space(10.0);

            // 3. 路径输入
            ui.label("3. 路径 (支持直接将文件拖拽到窗口):");
            ui.horizontal(|ui| {
                ui.label("源路径:");
                ui.text_edit_singleline(&mut self.source);
            });
            ui.horizontal(|ui| {
                ui.label("目标路径:");
                ui.text_edit_singleline(&mut self.target);
            });
            ui.add_space(15.0);

            // 4. 执行按钮
            ui.add_enabled_ui(!self.is_running, |ui| {
                if ui.button("🚀 开始执行").clicked() {
                    self.start_task();
                }
            });
            ui.add_space(10.0);

            // 5. 日志输出
            ui.label("运行日志:");
            egui::ScrollArea::vertical().show(ui, |ui| {
                ui.text_edit_multiline(&mut self.logs);
            });
        });

        // 持续刷新以获取日志
        if self.is_running {
            ctx.request_repaint();
        }
    }
}

impl MyApp {
    fn start_task(&mut self) {
        if self.source.is_empty() || self.target.is_empty() {
            let _ = self.log_tx.send("❌ 错误: 路径不能为空".to_string());
            return;
        }

        self.is_running = true;
        let source = self.source.clone();
        let target_dir = self.target.clone();
        let link_type = self.link_type.clone();
        let is_move = self.is_move;
        let overwrite = self.overwrite;
        let tx = self.log_tx.clone();

        thread::spawn(move || {
            let _ = tx.send("=== 任务开始 ===".to_string());
            let src_path = Path::new(&source);
            let file_name = src_path.file_name().unwrap_or_default();
            let mut final_link_path = target_dir.clone();
            let mut final_src_path = source.clone();

            if is_move {
                let _ = tx.send(format!("📦 准备搬家: {}", source));
                let dest_path = Path::new(&target_dir).join(file_name);
                
                if dest_path.exists() && overwrite {
                    let _ = fs::remove_file(&dest_path);
                    let _ = fs::remove_dir_all(&dest_path);
                }

                if let Err(e) = fs::rename(&source, &dest_path) {
                    let _ = tx.send(format!("❌ 移动失败: {}", e));
                    let _ = tx.send("DONE".to_string());
                    return;
                }
                
                // 搬家后，源位置变为空，我们需要在源位置创建链接指向新位置
                final_link_path = source.clone();
                final_src_path = dest_path.display().to_string();
            } else {
                // 原地分身模式
                final_link_path = Path::new(&target_dir).join(file_name).display().to_string();
            }

            let _ = tx.send(format!("🔗 正在执行: mklink {} \"{}\" \"{}\"", link_type, final_link_path, final_src_path));
            
            let output = Command::new("cmd")
                .args(["/c", "mklink", &link_type, &final_link_path, &final_src_path])
                .creation_flags(CREATE_NO_WINDOW)
                .output();

            match output {
                Ok(out) => {
                    let stdout = String::from_utf8_lossy(&out.stdout);
                    let stderr = String::from_utf8_lossy(&out.stderr);
                    if out.status.success() {
                        let _ = tx.send(format!("✅ 成功: {}", stdout.trim()));
                    } else {
                        let _ = tx.send(format!("❌ 失败: {}", stderr.trim()));
                    }
                }
                Err(e) => {
                    let _ = tx.send(format!("❌ 系统命令调用失败: {}", e));
                }
            }

            let _ = tx.send("DONE".to_string());
        });
    }
}
