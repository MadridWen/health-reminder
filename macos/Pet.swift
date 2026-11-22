import AppKit
import ImageIO

enum PetError: LocalizedError {
    case incompatibleImage
    case tooLarge
    case emptyImage
    case missingDefault

    var errorDescription: String? {
        switch self {
        case .incompatibleImage: return "无法读取这张 WebP 图片或动图。"
        case .tooLarge: return "WebP 图片最多支持 20 MiB、419 万像素和 120 帧。"
        case .emptyImage: return "图片或图集的第一行没有可见的宠物。"
        case .missingDefault: return "应用缺少默认宠物图片。"
        }
    }
}

struct PetFrame {
    let image: CGImage
    let delay: TimeInterval
    let visible: Bool
}

struct PetArt {
    static let displaySize = 112
    static let maximumBytes = 20 * 1024 * 1024
    static let maximumPixels = 4_194_304
    let idle: [PetFrame]
    var jump: [PetFrame] = []
    var waiting: [PetFrame] = []

    static func read(_ url: URL) throws -> Data {
        let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
        guard size >= 12, size <= maximumBytes else { throw PetError.tooLarge }
        return try Data(contentsOf: url, options: .mappedIfSafe)
    }

    static func builtIn() throws -> PetArt {
        guard let url = Bundle.main.url(forResource: "default-pet", withExtension: "webp") else {
            throw PetError.missingDefault
        }
        return try decode(read(url))
    }

    static func decode(_ data: Data) throws -> PetArt {
        guard data.count <= maximumBytes else { throw PetError.tooLarge }
        guard data.count >= 12, data.prefix(4).elementsEqual("RIFF".utf8),
              data[8..<12].elementsEqual("WEBP".utf8),
              let source = CGImageSourceCreateWithData(data as CFData, nil) else {
            throw PetError.incompatibleImage
        }
        let delays = animationDelays(data)
        let count = CGImageSourceGetCount(source)
        guard count >= 1, count >= delays.count else { throw PetError.incompatibleImage }
        guard count <= 120 else { throw PetError.tooLarge }
        let metadata = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [String: Any]
        guard let width = (metadata?[kCGImagePropertyPixelWidth as String] as? NSNumber)?.intValue,
              let height = (metadata?[kCGImagePropertyPixelHeight as String] as? NSNumber)?.intValue,
              width > 0, height > 0, width <= maximumPixels / height,
              width * height <= 180_000_000 / count else { throw PetError.tooLarge }

        if count == 1, width == 1_536, height == 1_872 || height == 2_288 {
            guard let atlas = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
                throw PetError.incompatibleImage
            }
            let idle = row(atlas, index: 0, delays: [0.28, 0.11, 0.11, 0.14, 0.14, 0.32])
            guard !idle.isEmpty else { throw PetError.emptyImage }
            return PetArt(idle: idle, jump: row(atlas, index: 4, delays: [0.14, 0.14, 0.14, 0.14, 0.28]),
                          waiting: row(atlas, index: 6, delays: [0.15, 0.15, 0.15, 0.15, 0.15, 0.26]))
        }

        var frames: [PetFrame] = []
        for index in 0..<count {
            let frame = try autoreleasepool { () throws -> PetFrame in
                guard let image = CGImageSourceCreateImageAtIndex(source, index, nil),
                      image.width > 0, image.height > 0, image.width <= maximumPixels / image.height,
                      let frame = scale(image, delay: index < delays.count ? delays[index] : 0.12) else {
                    throw PetError.incompatibleImage
                }
                return frame
            }
            frames.append(frame)
        }
        guard frames.contains(where: { $0.visible }) else { throw PetError.emptyImage }
        return PetArt(idle: frames)
    }

    private static func row(_ atlas: CGImage, index: Int, delays: [TimeInterval]) -> [PetFrame] {
        delays.enumerated().compactMap { column, delay in
            let rect = CGRect(x: column * 192, y: index * 208, width: 192, height: 208)
            guard let tile = atlas.cropping(to: rect), let frame = scale(tile, delay: delay),
                  frame.visible else { return nil }
            return frame
        }
    }

    private static func scale(_ image: CGImage, delay: TimeInterval) -> PetFrame? {
        let side = displaySize
        let layout = CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue
        guard let canvas = CGContext(data: nil, width: side, height: side,
                                     bitsPerComponent: 8, bytesPerRow: side * 4,
                                     space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: layout),
              let pixels = canvas.data?.assumingMemoryBound(to: UInt8.self) else { return nil }
        let ratio = CGFloat(side) / CGFloat(max(image.width, image.height))
        let width = CGFloat(image.width) * ratio
        let height = CGFloat(image.height) * ratio
        canvas.clear(CGRect(x: 0, y: 0, width: side, height: side))
        canvas.interpolationQuality = .medium
        canvas.draw(image, in: CGRect(x: (CGFloat(side) - width) / 2, y: (CGFloat(side) - height) / 2,
                                      width: width, height: height))
        let visible = (0..<(side * side)).contains { pixels[$0 * 4 + 3] != 0 }
        guard let bitmap = canvas.makeImage() else { return nil }
        return PetFrame(image: bitmap, delay: delay, visible: visible)
    }

    // ANMF stores each display duration as a 24-bit little-endian millisecond value.
    private static func animationDelays(_ data: Data) -> [TimeInterval] {
        var result: [TimeInterval] = []
        var cursor = 12
        while cursor <= data.count - 8 {
            let length = (0..<4).reduce(0) { $0 | Int(data[cursor + 4 + $1]) << (8 * $1) }
            let payload = cursor + 8
            guard length <= data.count - payload else { return result }
            if length >= 16 && data[cursor..<(cursor + 4)].elementsEqual("ANMF".utf8) {
                let millis = (0..<3).reduce(0) { $0 | Int(data[payload + 12 + $1]) << (8 * $1) }
                result.append(Double(min(60_000, max(80, millis))) / 1_000)
                if result.count > 120 { return result }
            }
            cursor = payload + length + length % 2
        }
        return result
    }
}

private final class PetView: NSView {
    var image: CGImage? { didSet { needsDisplay = true } }
    var menuProvider: (() -> NSMenu)?
    var positionChanged: (() -> Void)?
    var bounce: CGFloat = 0 { didSet { needsDisplay = true } }

    override func draw(_ dirtyRect: NSRect) {
        guard let image, let context = NSGraphicsContext.current?.cgContext else { return }
        context.clear(bounds)
        context.draw(image, in: bounds.offsetBy(dx: 0, dy: bounce))
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        window?.performDrag(with: event)
        positionChanged?()
    }

    override func rightMouseDown(with event: NSEvent) {
        if let menu = menuProvider?() {
            NSMenu.popUpContextMenu(menu, with: event, for: self)
        }
    }
}

@MainActor
final class DesktopPet {
    private let panel: NSPanel
    private let view: PetView
    private let art: PetArt
    private var timer: Timer?
    private var frames: [PetFrame]
    private var index = 0
    private var reacting = false
    private var bouncing = false
    private var waiting = false
    private var paused = false

    init(art: PetArt, menu: @escaping () -> NSMenu, positionChanged: @escaping (NSPoint) -> Void) {
        self.art = art
        frames = art.idle
        let side = PetArt.displaySize
        let rect = CGRect(x: 0, y: 0, width: side, height: side)
        view = PetView(frame: rect)
        panel = NSPanel(contentRect: rect, styleMask: [.borderless, .nonactivatingPanel],
                        backing: .buffered, defer: false)
        panel.contentView = view
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        view.menuProvider = menu
        view.positionChanged = { [weak self] in
            if let origin = self?.panel.frame.origin { positionChanged(origin) }
        }
    }

    func show(position: NSPoint?) {
        let screen = NSScreen.screens.first { screen in
            position.map { screen.visibleFrame.contains($0) } ?? false
        } ?? NSScreen.main
        if let frame = screen?.visibleFrame {
            let defaultOrigin = NSPoint(x: frame.maxX - panel.frame.width - 28, y: frame.minY + 120)
            let origin = position ?? defaultOrigin
            let x = max(frame.minX, min(frame.maxX - panel.frame.width, origin.x))
            let y = max(frame.minY, min(frame.maxY - panel.frame.height, origin.y))
            panel.setFrameOrigin(NSPoint(x: x, y: y))
        }
        panel.orderFrontRegardless()
        showIdle()
    }

    func close() {
        timer?.invalidate()
        timer = nil
        panel.orderOut(nil)
    }

    func setPaused(_ value: Bool) {
        paused = value
        if value {
            close()
        } else {
            panel.orderFrontRegardless()
            showIdle()
        }
    }

    func setWaiting(_ value: Bool) {
        guard value != waiting else { return }
        waiting = value
        if !reacting { showIdle() }
    }

    func celebrate() {
        guard !paused, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { return }
        reacting = true
        bouncing = art.jump.isEmpty
        let base = art.idle.first(where: { $0.visible }) ?? art.idle[0]
        frames = bouncing ? Array(repeating: PetFrame(image: base.image, delay: 0.10, visible: true), count: 3) : art.jump
        index = 0
        drawFrame()
    }

    private func showIdle() {
        reacting = false
        bouncing = false
        frames = waiting && !art.waiting.isEmpty ? art.waiting : art.idle
        index = frames.firstIndex(where: { $0.visible }) ?? 0
        drawFrame()
    }

    private func drawFrame() {
        timer?.invalidate()
        timer = nil
        guard !paused else { return }
        if NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            view.image = frames.first(where: { $0.visible })?.image
            view.bounce = 0
            return
        }
        view.image = frames[index].image
        view.bounce = bouncing ? [5, 12, 5][index] : 0
        guard frames.count > 1 || reacting else { return }
        let next = Timer(timeInterval: frames[index].delay, repeats: false) { [weak self] _ in
            Task { @MainActor [weak self] in self?.advance() }
        }
        timer = next
        RunLoop.main.add(next, forMode: .common)
    }

    private func advance() {
        index += 1
        if index == frames.count {
            if reacting {
                showIdle()
                return
            }
            index = 0
        }
        drawFrame()
    }

    deinit { timer?.invalidate() }
}
