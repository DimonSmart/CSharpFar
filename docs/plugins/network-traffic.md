# Network Traffic

Open **Plugins → Network Traffic...**, or enter `traffic` on the command line.
On Windows, start CSharpFar as administrator. The plugin performs no automatic
elevation and never changes audit policy. Other platforms show an availability
message. Demo mode uses samples without OS collection.

The list sorts applications by received + sent bytes per second over a rolling
ten-second window. Expect roughly five seconds of ETW delivery/settling delay.
The main table shows readable application names, available window captions and
current receive/send rates and separate Downloaded/Uploaded totals from the start
of monitoring. Names use executable file descriptions; captions
use the standard .NET process API. Background processes and browser network
subprocesses may have no main window. Processes sharing an executable path remain
one application, regardless of name or caption. Loopback is included, so this is
not an Internet-only or billing counter.

Type in Filter to match application, window caption, path or process identity.
All rates and directional totals are visible in the main table. Enter does not
open details. Approximate attribution means the owner was inferred from matching
connection snapshots; the byte count itself is measured. Esc or F10 closes the
screen and stops its tracing session.

Unknown bytes remain separate. A newly accepted TCP socket may have some startup
bytes in Unknown until endpoint snapshots can bracket its traffic. Estimated bytes use matching snapshots around
the event for already-open sockets; they are not exact lifecycle evidence.
The status warns when Unknown could change the leader or collection loses events.
Reopen after a degraded capture to begin a fresh session. VPN, containers and
virtualized traffic have not been validated.

The module lives in `CSharpFar.Module.NetworkTraffic`; the collector and models
live in `CSharpFar.NetworkTraffic`. The application explicitly composes it through
`ICSharpFarPlugin`, exactly like other first-party plugins. There is no dynamic
DLL discovery. Shared UI services are constructor-injected; no application
implementation types enter the plugin contract.

## Validation scope

The Windows host builds successfully. Automated application, UI and architecture
suites cover registration, demo isolation, aggregation/rate decay, lifecycle and
PID reuse, Unknown on unreliable evidence, external TCP tuple orientation,
selection, adaptive resize and cancellation. A real elevated UDP capture passed
for both new and pre-existing sockets, including owned-session cleanup.

An initial immediate-start TCP case attributed 94.5% of the controlled bytes;
its startup remainder stayed Unknown. The sustained-stream tests now allow an
endpoint-snapshot warm-up. The final elevated repeat was cancelled at Windows
UAC, so the latest TCP/UDP live repeat and a manual real-terminal UI review are
not verified.

