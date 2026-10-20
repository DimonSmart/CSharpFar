import AppKit
import Darwin
import SwiftTerm

// The process in this window is the existing portable CSharpFar, not a second
// implementation of the file manager. SwiftTerm owns the PTY and VT surface.
final class CSharpFarDesktopHost: NSObject, NSApplicationDelegate, NSWindowDelegate,
    LocalProcessTerminalViewDelegate, NSMenuItemValidation {

    private var window: NSWindow?
    private var terminal: LocalProcessTerminalView?
    private var shutdownConfirmed = false
    private var terminateRequested = false
    private var childHasExited = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        installMenu()

        let resources = Bundle.main.resourceURL
        guard let executable = resources?.appendingPathComponent("csharpfar").path,
              FileManager.default.isExecutableFile(atPath: executable) else {
            displayStartupFailure("The bundled CSharpFar executable is missing or is not executable.")
            return
        }

        let frame = NSRect(x: 0, y: 0, width: 1100, height: 720)
        let window = NSWindow(contentRect: frame,
                              styleMask: [.titled, .closable, .miniaturizable, .resizable],
                              backing: .buffered, defer: false)
        window.title = "CSharpFar"
        window.center()
        window.delegate = self
        window.tabbingMode = .disallowed
        self.window = window

        let terminal = LocalProcessTerminalView(frame: window.contentView!.bounds)
        terminal.autoresizingMask = [.width, .height]
        terminal.font = NSFont.monospacedSystemFont(ofSize: 14, weight: .regular)
        terminal.nativeForegroundColor = .textColor
        terminal.nativeBackgroundColor = .textBackgroundColor
        terminal.optionAsMetaKey = true
        terminal.processDelegate = self
        window.contentView?.addSubview(terminal)
        self.terminal = terminal
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(terminal)
        NSApp.activate(ignoringOtherApps: true)

        // Preserve the user's real environment rather than SwiftTerm's default
        // reduced environment. The PTY provides stdin, stdout, stderr and a
        // controlling terminal; the CLI handles command child foreground groups.
        var environment = ProcessInfo.processInfo.environment
        environment["TERM"] = "xterm-256color"
        environment["COLORTERM"] = "truecolor"
        if environment["LANG"] == nil { environment["LANG"] = "en_US.UTF-8" }
        let env = environment.map { "\($0.key)=\($0.value)" }
        let args = Array(CommandLine.arguments.dropFirst()).filter { !$0.hasPrefix("-psn_") }
        terminal.startProcess(executable: executable, args: args, environment: env,
                              currentDirectory: FileManager.default.homeDirectoryForCurrentUser.path)
        if !terminal.process.running {
            displayStartupFailure("Unable to start CSharpFar in a pseudo-terminal.")
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        return true
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        return requestShutdown() ? .terminateNow : .terminateCancel
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        return requestShutdown()
    }

    func applicationWillTerminate(_ notification: Notification) {
        // Only an explicit close of an active foreground child is forceful.
        // If macOS terminates the host externally, clean up the PTY session.
        if !childHasExited { endSession() }
    }

    private func requestShutdown() -> Bool {
        if shutdownConfirmed || childHasExited { return true }
        guard let terminal, terminal.process.running else { return true }

        if hasForegroundChild(terminal) {
            let alert = NSAlert()
            alert.messageText = "A terminal command is still running"
            alert.informativeText = "Terminate the command and close CSharpFar, or keep the session running?"
            alert.alertStyle = .warning
            alert.addButton(withTitle: "Cancel")
            alert.addButton(withTitle: "Terminate Session")
            if alert.runModal() != .alertSecondButtonReturn { return false }
            shutdownConfirmed = true
            endSession()
            return true
        }

        // Request the application's normal F10 exit route. CSharpFar owns its
        // unsaved editor/operation confirmation; cancelled exit leaves the PTY
        // and its window entirely intact. No unconditional kill timer here.
        if !terminateRequested {
            terminateRequested = true
            let f10 = Array("\u{001B}[21~".utf8)
            terminal.process.send(data: f10[...])
            DispatchQueue.main.asyncAfter(deadline: .now() + .milliseconds(350)) { [weak self] in
                self?.terminateRequested = false
            }
        }
        return false
    }

    private func hasForegroundChild(_ terminal: LocalProcessTerminalView) -> Bool {
        let descriptor = terminal.process.childfd
        let leader = terminal.process.shellPid
        guard descriptor >= 0, leader > 0 else { return false }
        let foreground = tcgetpgrp(descriptor)
        return foreground > 0 && foreground != leader
    }

    private func endSession() {
        guard let terminal, terminal.process.running else { return }
        let fd = terminal.process.childfd
        let leader = terminal.process.shellPid
        if fd >= 0 {
            let foreground = tcgetpgrp(fd)
            if foreground > 0 && foreground != leader { _ = kill(-foreground, SIGTERM) }
        }
        if leader > 0 { _ = kill(-leader, SIGTERM) }
        terminal.terminate()
    }

    func processTerminated(source: TerminalView, exitCode: Int32?) {
        guard !childHasExited else { return }
        childHasExited = true
        if !shutdownConfirmed && (exitCode == nil || exitCode != 0) {
            let alert = NSAlert()
            alert.messageText = "CSharpFar stopped unexpectedly"
            alert.informativeText = exitCode.map { "The CSharpFar process terminated (wait status \($0))." }
                ?? "The terminal connection closed unexpectedly."
            alert.alertStyle = .critical
            alert.addButton(withTitle: "Close")
            alert.runModal()
        }
        shutdownConfirmed = true
        window?.close()
        NSApp.terminate(nil)
    }

    func sizeChanged(source: LocalProcessTerminalView, newCols: Int, newRows: Int) {
        // SwiftTerm already resizes the PTY and sends SIGWINCH.
    }

    func setTerminalTitle(source: LocalProcessTerminalView, title: String) {
        window?.title = title.isEmpty ? "CSharpFar" : "CSharpFar — \(title)"
    }

    func hostCurrentDirectoryUpdate(source: TerminalView, directory: String?) {}

    private func displayStartupFailure(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "CSharpFar could not start"
        alert.informativeText = message
        alert.alertStyle = .critical
        alert.addButton(withTitle: "Close")
        alert.runModal()
        childHasExited = true
        shutdownConfirmed = true
        window?.close()
        NSApp.terminate(nil)
    }

    private func installMenu() {
        let main = NSMenu()
        let app = NSMenuItem()
        main.addItem(app)
        let appMenu = NSMenu()
        appMenu.addItem(NSMenuItem(title: "Quit CSharpFar",
                                   action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        app.submenu = appMenu
        let edit = NSMenuItem()
        main.addItem(edit)
        let editMenu = NSMenu(title: "Edit")
        let copy = NSMenuItem(title: "Copy", action: #selector(copySelection(_:)), keyEquivalent: "c")
        copy.target = self
        editMenu.addItem(copy)
        let paste = NSMenuItem(title: "Paste", action: #selector(pasteClipboard(_:)), keyEquivalent: "v")
        paste.target = self
        editMenu.addItem(paste)
        edit.submenu = editMenu
        NSApp.mainMenu = main
    }

    func validateMenuItem(_ item: NSMenuItem) -> Bool {
        if item.action == #selector(copySelection(_:)) {
            return terminal?.selectionActive == true
        }
        return true
    }

    @objc private func copySelection(_ sender: Any?) {
        guard let terminal, terminal.selectionActive else { return }
        terminal.copy(sender ?? self)
    }

    @objc private func pasteClipboard(_ sender: Any?) {
        terminal?.paste(sender ?? self)
    }
}

private let application = NSApplication.shared
private let controller = CSharpFarDesktopHost()
application.delegate = controller
application.run()
