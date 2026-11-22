import AppKit
import ServiceManagement
import SwiftUI
import UserNotifications

private let appName = "健康提醒"
private let waterIdentifier = "water-reminder"
private let movementIdentifier = "movement-reminder"

@MainActor
final class ReminderApp: NSObject, NSApplicationDelegate, NSMenuDelegate, UNUserNotificationCenterDelegate {
    private let defaults = UserDefaults.standard
    private let schedule: ReminderSchedule
    private var timing: Timing
    private var menuBarItem: NSStatusItem?
    private var settingsWindow: NSWindow?
    private var pet: DesktopPet?
    private var displayedArt: PetArt?
    private var timer: Timer?
    private var lastDrink: Date?
    private var waterPending = false
    private var inactiveSession = false
    private var sleeping = false

    private static var now: TimeInterval { ProcessInfo.processInfo.systemUptime }

    override init() {
        let saved = Timing(defaults: .standard)
        timing = saved
        schedule = ReminderSchedule(timing: saved, now: Self.now)
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        let icon = NSImage(systemSymbolName: "drop.fill", accessibilityDescription: appName)
        icon?.isTemplate = true
        item.button?.image = icon
        item.button?.toolTip = appName
        item.menu = makeMenu()
        menuBarItem = item

        let notifications = UNUserNotificationCenter.current()
        notifications.delegate = self
        notifications.getNotificationSettings { state in
            if state.authorizationStatus == .notDetermined {
                notifications.requestAuthorization(options: [.alert, .sound]) { _, _ in }
            }
        }

        let workspace = NSWorkspace.shared.notificationCenter
        workspace.addObserver(self, selector: #selector(willSleep), name: NSWorkspace.willSleepNotification, object: nil)
        workspace.addObserver(self, selector: #selector(didWake), name: NSWorkspace.didWakeNotification, object: nil)
        workspace.addObserver(self, selector: #selector(sessionInactive), name: NSWorkspace.sessionDidResignActiveNotification, object: nil)
        workspace.addObserver(self, selector: #selector(sessionActive), name: NSWorkspace.sessionDidBecomeActiveNotification, object: nil)

        if defaults.bool(forKey: "petEnabled"), let art = try? loadSavedArt() { showPet(art) }
        let check = Timer(timeInterval: 5, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in self?.poll() }
        }
        check.tolerance = 1
        timer = check
        RunLoop.main.add(check, forMode: .common)
    }

    @objc private func willSleep(_ notification: Notification) {
        sleeping = true
        changeSession()
    }

    @objc private func didWake(_ notification: Notification) {
        sleeping = false
        changeSession()
    }

    @objc private func sessionInactive(_ notification: Notification) {
        inactiveSession = true
        changeSession()
    }

    @objc private func sessionActive(_ notification: Notification) {
        inactiveSession = false
        changeSession()
    }

    private func changeSession() {
        schedule.resetMovement(now: Self.now)
        pet?.setPaused(sleeping || inactiveSession)
    }

    private func poll() {
        guard !sleeping, !inactiveSession else { return }
        let types: [CGEventType] = [.keyDown, .flagsChanged, .mouseMoved, .scrollWheel, .leftMouseDown,
                                   .rightMouseDown, .otherMouseDown, .leftMouseDragged, .rightMouseDragged,
                                   .otherMouseDragged]
        let idle = types.map { CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: $0) }.min()
        let now = Self.now
        let due = schedule.check(now: now, idle: idle)
        guard !due.isEmpty else { return }

        let content = UNMutableNotificationContent()
        content.sound = .default
        let includesWater = due.contains(.water)
        if includesWater && due.contains(.movement) {
            content.title = "起来活动，喝口水"
            content.body = "起身换个姿势，顺便喝几口水。喝完后在菜单栏或桌面宠物菜单中选择「已喝水」。"
        } else if includesWater {
            content.title = "该喝水了"
            content.body = "喝完后在菜单栏或桌面宠物菜单中选择「已喝水」。"
        } else {
            content.title = "起来动一动"
            content.body = "站起来换个姿势、伸展一下，或走几步。"
        }
        let key = includesWater ? waterIdentifier : movementIdentifier
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: key, content: content, trigger: nil))
        schedule.markReminded(due, now: now)
        if includesWater {
            waterPending = true
            menuBarItem?.button?.toolTip = appName + " · 喝水待确认"
            pet?.setWaiting(true)
        }
    }

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter, willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .list, .sound])
    }

    private func makeMenu() -> NSMenu {
        let menu = NSMenu()
        menu.delegate = self
        menu.autoenablesItems = false
        addItem("已喝水", action: #selector(confirmWater), to: menu)
        for tag in [1, 2] {
            let status = NSMenuItem(title: "", action: nil, keyEquivalent: "")
            status.tag = tag
            status.isEnabled = false
            menu.addItem(status)
        }
        menu.addItem(.separator())
        addItem("设置与桌面宠物…", action: #selector(openSettings), to: menu)
        addItem("退出健康提醒", action: #selector(quit), to: menu)
        menuNeedsUpdate(menu)
        return menu
    }

    private func addItem(_ title: String, action: Selector, to menu: NSMenu) {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        menu.addItem(item)
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        var waterStatus = "本次运行尚未确认喝水"
        if waterPending {
            waterStatus = "喝水提醒 · 待确认"
        } else if let lastDrink {
            let formatter = DateFormatter()
            formatter.dateFormat = Calendar.current.isDateInToday(lastDrink) ? "HH:mm" : "MM-dd HH:mm"
            waterStatus = "上次喝水 · " + formatter.string(from: lastDrink)
        }
        menu.item(withTag: 1)?.title = waterStatus
        let seconds = schedule.remainingWater(now: Self.now)
        menu.item(withTag: 2)?.title = seconds == 0 ? "下次喝水 · 即将提醒"
            : String(format: "下次喝水 · %d 分 %02d 秒后", seconds / 60, seconds % 60)
    }

    @objc private func confirmWater() {
        schedule.confirmWater(now: Self.now)
        lastDrink = Date()
        waterPending = false
        UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: [waterIdentifier])
        menuBarItem?.button?.toolTip = appName
        pet?.setWaiting(false)
        pet?.celebrate()
    }

    @objc private func quit() { NSApplication.shared.terminate(nil) }

    private var imageURL: URL {
        let manager = FileManager.default
        let folder = manager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? manager.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support")
        return folder.appendingPathComponent("HealthReminder", isDirectory: true)
            .appendingPathComponent("desktop-pet.webp")
    }

    private func loadSavedArt() throws -> PetArt {
        if FileManager.default.fileExists(atPath: imageURL.path) {
            return try PetArt.decode(PetArt.read(imageURL))
        }
        return try PetArt.builtIn()
    }

    private func showPet(_ art: PetArt) {
        pet?.close()
        let pet = DesktopPet(art: art, menu: { [weak self] in self?.makeMenu() ?? NSMenu() },
                             positionChanged: { [weak self] point in
            self?.defaults.set([Double(point.x), Double(point.y)], forKey: "petPosition")
        })
        self.pet = pet
        displayedArt = art
        let coordinates = defaults.array(forKey: "petPosition") as? [Double]
        let position = coordinates.flatMap { values -> NSPoint? in
            guard values.count == 2, values.allSatisfy({ $0.isFinite && abs($0) <= 100_000 }) else { return nil }
            return NSPoint(x: values[0], y: values[1])
        }
        pet.setWaiting(waterPending)
        pet.show(position: position)
        if sleeping || inactiveSession { pet.setPaused(true) }
    }

    private var loginRegistered: Bool {
        let status = SMAppService.mainApp.status
        return status == .enabled || status == .requiresApproval
    }

    @objc private func openSettings() {
        if let settingsWindow, settingsWindow.isVisible {
            NSApplication.shared.activate(ignoringOtherApps: true)
            settingsWindow.makeKeyAndOrderFront(nil)
            return
        }

        let logo = FileManager.default.fileExists(atPath: imageURL.path) ? "当前形象 · 自定义 WebP" : "当前形象 · 默认小水滴"
        let view = SettingsView(movement: String(timing.movement), water: String(timing.water),
                                rest: String(timing.rest), launchAtLogin: loginRegistered,
                                showPet: defaults.bool(forKey: "petEnabled"), imageLabel: logo,
                                loginNeedsApproval: SMAppService.mainApp.status == .requiresApproval,
                                save: { [weak self] next, launch, enabled, selection in
            try self?.save(next, launch: launch, enabled: enabled, selection: selection)
        }, close: { [weak self] in self?.settingsWindow?.close() })
        let window = NSWindow(contentViewController: NSHostingController(rootView: view))
        window.title = "健康提醒设置"
        window.styleMask = [.titled, .closable]
        window.isReleasedWhenClosed = false
        window.center()
        settingsWindow = window
        NSApplication.shared.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    private func save(_ next: Timing, launch: Bool, enabled: Bool, selection: PetSelection) throws {
        let art: PetArt?
        switch (enabled, selection) {
        case (false, _): art = nil
        case (true, .keep): art = try displayedArt ?? loadSavedArt()
        case (true, .builtIn): art = try PetArt.builtIn()
        case (true, .imported(_, let selected)): art = selected
        }
        let previousLogin = loginRegistered
        if previousLogin != launch { try updateLogin(launch) }
        do {
            switch selection {
            case .keep: break
            case .builtIn:
                if FileManager.default.fileExists(atPath: imageURL.path) {
                    try FileManager.default.removeItem(at: imageURL)
                }
            case .imported(let data, _):
                try FileManager.default.createDirectory(at: imageURL.deletingLastPathComponent(), withIntermediateDirectories: true)
                try data.write(to: imageURL, options: .atomic)
            }
        } catch {
            if previousLogin != launch { try? updateLogin(previousLogin) }
            throw error
        }
        timing = next
        timing.save(to: defaults)
        schedule.apply(next, now: Self.now)
        defaults.set(enabled, forKey: "petEnabled")
        if let art {
            showPet(art)
        } else {
            pet?.close()
            pet = nil
            displayedArt = nil
        }
    }

    private func updateLogin(_ enabled: Bool) throws {
        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }
}

private func runChecks() throws {
    let schedule = ReminderSchedule(timing: Timing(movement: 1, water: 1, rest: 2), now: 1)
    precondition(schedule.check(now: 2, idle: 0).isEmpty)
    precondition(schedule.check(now: 31, idle: 0).isEmpty)
    precondition(schedule.check(now: 62, idle: 0) == [.movement, .water])
    schedule.markReminded([.movement, .water], now: 62)
    schedule.confirmWater(now: 90)
    precondition(schedule.remainingWater(now: 90) == 60)
    precondition(schedule.check(now: 123, idle: nil).isEmpty)
    precondition(schedule.check(now: 150, idle: nil) == .water)
    schedule.resetMovement(now: 160)
    precondition(schedule.check(now: 161, idle: 2) == .water)
    let art = try PetArt.builtIn()
    precondition(art.idle.count == 6 && art.jump.count == 5 && art.waiting.count == 6)
    let still = Data(base64Encoded: "UklGRh4AAABXRUJQVlA4TBEAAAAvA8AAAAdQsmaUu/+BiOh/AAA=")!
    let animated = Data(base64Encoded: "UklGRogAAABXRUJQVlA4WAoAAAACAAAAAwAAAwAAQU5JTQYAAAAAAAAAAABBTk1GKgAAAAAAAAAAAAMAAAMAAG4AAAJWUDhMEQAAAC8DwAAAB1CyZpS7/4GI6H8AAEFOTUYqAAAAAAAAAAAAAwAAAwAAqgAAAFZQOEwRAAAALwPAAAAHULJyN6P/gYjofwAA")!
    let oneFrame = try PetArt.decode(still)
    let motion = try PetArt.decode(animated)
    precondition(oneFrame.idle.count == 1 && motion.idle.map(\.delay) == [0.11, 0.17])
    print("macOS checks passed: activity, reminder reset, and static, animated, and atlas WebP decoding.")
}

@MainActor
private func smokeTest() throws {
    let app = NSApplication.shared
    precondition(app.setActivationPolicy(.accessory))
    let bar = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    precondition(bar.button != nil)
    let pet = DesktopPet(art: try PetArt.builtIn(), menu: { NSMenu() }, positionChanged: { _ in })
    pet.show(position: nil)
    let settings = SettingsView(movement: "45", water: "60", rest: "5", launchAtLogin: false,
                                showPet: true, imageLabel: "默认小水滴", loginNeedsApproval: false,
                                save: { _, _, _, _ in }, close: {})
    let window = NSWindow(contentViewController: NSHostingController(rootView: settings))
    window.isReleasedWhenClosed = false
    window.orderFrontRegardless()
    RunLoop.main.run(until: Date().addingTimeInterval(0.25))
    precondition(window.isVisible)
    window.close()
    pet.close()
    NSStatusBar.system.removeStatusItem(bar)
    print("macOS GUI smoke test passed: menu bar item, settings, and transparent pet window.")
}

@main
struct HealthReminder {
    @MainActor
    static func main() {
        do {
            if CommandLine.arguments.contains("--self-test") {
                try runChecks()
            } else if CommandLine.arguments.contains("--smoke-test") {
                try smokeTest()
            } else {
                let app = NSApplication.shared
                let delegate = ReminderApp()
                app.setActivationPolicy(.accessory)
                app.delegate = delegate
                app.run()
            }
        } catch {
            fputs(error.localizedDescription + "\n", stderr)
            exit(1)
        }
    }
}
