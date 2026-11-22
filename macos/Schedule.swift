import Foundation

struct Timing {
    var movement = 45
    var water = 60
    var rest = 5

    static func valid(_ minutes: Int) -> Bool {
        (1...1_440).contains(minutes)
    }

    init() {}

    init(movement: Int, water: Int, rest: Int) {
        self.movement = movement
        self.water = water
        self.rest = rest
    }

    init(defaults: UserDefaults) {
        let movement = defaults.integer(forKey: "movementMinutes")
        let water = defaults.integer(forKey: "waterMinutes")
        let rest = defaults.integer(forKey: "breakMinutes")
        if Self.valid(movement) { self.movement = movement }
        if Self.valid(water) { self.water = water }
        if Self.valid(rest) { self.rest = rest }
    }

    func save(to defaults: UserDefaults) {
        defaults.set(movement, forKey: "movementMinutes")
        defaults.set(water, forKey: "waterMinutes")
        defaults.set(rest, forKey: "breakMinutes")
    }
}

struct ReminderKind: OptionSet {
    let rawValue: UInt8
    static let movement = ReminderKind(rawValue: 1)
    static let water = ReminderKind(rawValue: 2)
}

final class ReminderSchedule {
    private var timing: Timing
    private var movementEligibleAt: TimeInterval
    private var movementStartedAt: TimeInterval?
    private var lastInputAt: TimeInterval?
    private var nextWater: TimeInterval

    init(timing: Timing, now: TimeInterval) {
        self.timing = timing
        movementEligibleAt = now
        nextWater = now + Double(timing.water * 60)
    }

    func resetMovement(now: TimeInterval) {
        movementEligibleAt = now
        movementStartedAt = nil
        lastInputAt = nil
    }

    func confirmWater(now: TimeInterval) {
        nextWater = now + Double(timing.water * 60)
    }

    func remainingWater(now: TimeInterval) -> Int {
        Int(max(0, (nextWater - now).rounded(.up)))
    }

    func apply(_ newTiming: Timing, now: TimeInterval) {
        let previous = timing
        timing = newTiming
        if previous.movement != timing.movement || previous.rest != timing.rest {
            resetMovement(now: now)
        }
        if previous.water != timing.water {
            confirmWater(now: now)
        }
    }

    func check(now: TimeInterval, idle: TimeInterval?) -> ReminderKind {
        var due: ReminderKind = now >= nextWater ? .water : []
        guard let idle, idle.isFinite, idle >= 0 else { return due }

        let rest = Double(timing.rest * 60)
        if idle >= rest {
            resetMovement(now: now)
            return due
        }

        let input = now - idle
        guard input >= movementEligibleAt else { return due }
        if lastInputAt.map({ input - $0 >= rest }) ?? true {
            movementStartedAt = input
        }
        lastInputAt = input

        if let started = movementStartedAt, idle <= 15, now - started >= Double(timing.movement * 60) {
            due.insert(.movement)
        }
        return due
    }

    func markReminded(_ due: ReminderKind, now: TimeInterval) {
        if due.contains(.movement) { movementStartedAt = now }
        if due.contains(.water) { confirmWater(now: now) }
    }
}
