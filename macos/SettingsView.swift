import AppKit
import SwiftUI
import UniformTypeIdentifiers

enum PetSelection {
    case keep
    case builtIn
    case imported(Data, PetArt)
}

struct SettingsView: View {
    @State var movement: String
    @State var water: String
    @State var rest: String
    @State var launchAtLogin: Bool
    @State var showPet: Bool
    @State var imageLabel: String
    @State private var choice = PetSelection.keep
    @State private var feedback = ""
    let loginNeedsApproval: Bool
    let save: (Timing, Bool, Bool, PetSelection) throws -> Void
    let close: () -> Void

    init(movement: String, water: String, rest: String, launchAtLogin: Bool,
         showPet: Bool, imageLabel: String, loginNeedsApproval: Bool,
         save: @escaping (Timing, Bool, Bool, PetSelection) throws -> Void, close: @escaping () -> Void) {
        _movement = State(initialValue: movement)
        _water = State(initialValue: water)
        _rest = State(initialValue: rest)
        _launchAtLogin = State(initialValue: launchAtLogin)
        _showPet = State(initialValue: showPet)
        _imageLabel = State(initialValue: imageLabel)
        self.loginNeedsApproval = loginNeedsApproval
        self.save = save
        self.close = close
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            GroupBox("提醒间隔") {
                VStack(alignment: .leading, spacing: 9) {
                    interval("起身提醒", $movement)
                    interval("喝水提醒", $water)
                    interval("离开多久算休息", $rest)
                }
                .padding(8)
            }
            GroupBox("桌面宠物") {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle("显示桌面宠物", isOn: $showPet)
                    Text(imageLabel).foregroundStyle(.secondary).lineLimit(1)
                    HStack {
                        Button("导入 WebP…", action: importImage)
                        Button("恢复默认") {
                            choice = .builtIn
                            imageLabel = "已选择默认小水滴"
                            feedback = "保存后应用更改。"
                        }
                    }
                    HStack {
                        Button("复制单图提示词") { copy(Self.imagePrompt) }
                        Button("复制动画图集提示词") { copy(Self.atlasPrompt) }
                    }
                }
                .padding(8)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            Toggle("开机登录后自动启动", isOn: $launchAtLogin)
            if loginNeedsApproval {
                Text("请在 macOS 系统设置的登录项中允许健康提醒。")
                    .foregroundStyle(.secondary)
            }
            if !feedback.isEmpty {
                Text(feedback).foregroundStyle(.secondary)
            }
            HStack {
                Spacer()
                Button("取消", action: close).keyboardShortcut(.cancelAction)
                Button("保存", action: saveChanges).keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 475)
    }

    private func interval(_ label: String, _ value: Binding<String>) -> some View {
        HStack {
            Text(label)
            Spacer()
            TextField(label, text: value)
                .labelsHidden()
                .textFieldStyle(.roundedBorder)
                .multilineTextAlignment(.trailing)
                .frame(width: 62)
            Text("分钟")
        }
    }

    private func saveChanges() {
        guard let move = Int(movement.trimmingCharacters(in: .whitespaces)),
              let drink = Int(water.trimmingCharacters(in: .whitespaces)),
              let idle = Int(rest.trimmingCharacters(in: .whitespaces)),
              [move, drink, idle].allSatisfy(Timing.valid) else {
            feedback = "提醒间隔请输入 1 到 1440 之间的整数。"
            return
        }
        do {
            try save(Timing(movement: move, water: drink, rest: idle), launchAtLogin, showPet, choice)
            close()
        } catch {
            feedback = error.localizedDescription
        }
    }

    private func importImage() {
        let picker = NSOpenPanel()
        picker.title = "选择宠物 WebP 图片"
        picker.allowedContentTypes = [.webP]
        picker.allowsMultipleSelection = false
        picker.canChooseDirectories = false
        guard picker.runModal() == .OK, let url = picker.url else { return }
        do {
            let data = try PetArt.read(url)
            choice = .imported(data, try PetArt.decode(data))
            showPet = true
            imageLabel = "已选择 " + url.lastPathComponent
            feedback = "保存后在桌面显示这只宠物。"
        } catch {
            feedback = error.localizedDescription
        }
    }

    private func copy(_ prompt: String) {
        NSPasteboard.general.clearContents()
        feedback = NSPasteboard.general.setString(prompt, forType: .string)
            ? "提示词已复制，可以粘贴到 AI 绘图工具。"
            : "无法写入剪贴板，请重试。"
    }

    private static let imagePrompt = """
    为桌面应用画一只原创宠物。角色描述：[替换为你想要的角色]。
    生成 512×512 像素的正面全身图，居中放置，四周留 10% 的透明空间。
    使用真正的 Alpha 透明背景。不要地面、独立投影、场景、文字、水印或棋盘格。
    交付透明 WebP。若只能输出 PNG，保留 Alpha 通道再无损转换成 WebP。
    """

    private static let atlasPrompt = """
    为桌面应用制作原创宠物动画图集。角色描述：[替换为你想要的角色]。
    画布正好为 1536×1872 像素，8 列×9 行，每格 192×208 像素。
    第 1 行前 6 格为待机眨眼，第 5 行前 5 格为开心跳跃，第 7 行前 6 格为等待喝水。
    每行从左向右播放。其余格子完全透明。保持角色造型、大小和站位一致。
    不加留缝、分格线、标签、背景、独立阴影或水印。交付无损 Alpha 透明 WebP。
    """
}
