# Network Traffic ETW feasibility — blocking Windows spike

Status: **NO-GO for the selected payload-PID extraction** (2026-10-22).
This is a research probe, **not** the requested end-user Network Traffic plugin.

## Hypothesis and limits

An independent uniquely named, real-time kernel ETW session
(\`TraceEventSession.EnableKernelProvider(NetworkTCPIP | Process)\`) might
report TCP/UDP Send/Receive packet lengths and trustworthy process identities
without owning the system's \`NT Kernel Logger\` or other sessions. Before a
privileged collector or live UI is built, attribution must be demonstrated
from reliable network-event data and the semantics of sizes must be understood.
Never silently substitute the ETW **header** PID for a missing event-owned PID.

## Reproduction

Run on Windows 10/11 x64 with a .NET 10 SDK:

\`\`\`powershell
dotnet run --project tools/NetworkTraffic.EtwSpike/NetworkTraffic.EtwSpike.csproj -- --output etw-spike-result.json
\`\`\`

Run under both normal and elevated tokens on actual Windows installations;
the probe never elevates itself. It generates loopback TCP and UDP payloads
over IPv4 and IPv6, waits for real-time ETW buffer delivery, then writes JSON
with counts, per-type payload schema, PID/size validation, sampling lag and
loss diagnostics. It collects no packet contents or address listings.
The ETW name is unique and the probe stops **only its own** created session
on normal disposal. A crash/orphan verification has not been performed.

An independent Windows-only workflow can be run from GitHub Actions and
uploads the diagnostic JSON **even if the spike exits unsuccessfully**.
A passing application build does not mean this gate has passed.

## Empirical results — Windows 11, build 26100, hosted GitHub Actions

- The initial prototype was rejected by TraceEvent: kernel provider activation
  must precede access to \`session.Source\`. That probe initialization defect
  was fixed, and the subsequent standalone session started successfully.
- The fixed probe successfully generated TCP and UDP IPv4/IPv6 loopback data.
  The Windows runner emitted 530 network Send/Receive ETW events across
  the four protocol/direction combinations in IPv4 and IPv6, including
  \`TcpIp/Send\`, \`TcpIp/Recv\`, \`UdpIp/Send\`, \`UdpIp/Recv\` and
  the corresponding \`IPV6\` variants.
- Recorded \`size\` fields matched individual submitted payload lengths in
  sampled events (8,192 bytes TCP; 1,024 bytes UDP), but comprehensive byte
  semantics, retransmits and comparison with Resource Monitor remain unverified.
- **Blocking mismatch:** all eight observed Send/Receive payload schemas
  were reported as ETW version 2, **without a \`PID\` payload field**.
  Across 530 events the probe could not validate a single payload PID.
  The header ProcessID happened to match the generating process in this one
  loopback case; the specification expressly forbids treating this as proof
  of correct attribution under arbitrary network activity.
- \`source.EventsLost\` was zero in this short run. That counter alone is not
  a proof of no dropped buffers or kernel/logger loss, particularly under load.
- The test did **not** validate normal-user privileges, UAC, independent
  child processes, PID reuse, short-lived processes, event-delivery lag
  statistics, VPN/proxy behavior, remote traffic, system trace session limits,
  alternate-SID elevation or crash/orphan cleanup.

Evidence:

- [First live kernel ETW observation](https://github.com/DimonSmart/CSharpFar/actions/runs/39775584111)
- [Version-2 network event schema and missing PID](https://github.com/DimonSmart/CSharpFar/actions/runs/39776460071)

The original diagnostic incorrectly labeled events with missing PID as invalid
payloads. The probe now counts **observed** events separately from events
with validated attribution, and samples delivery lag independently from PID
so that future runs provide usable evidence even on a No-Go.

## Decision and next steps

**NO-GO for a production per-application Network Traffic plugin based on the
currently selected event-PID extraction.** TCP/UDP byte events are present;
the necessary reliable process identity has not yet been demonstrated. The
proof is insufficient to implement app grouping or process rates without
risking false attribution. This is **not** a general No-Go for Windows
network monitoring.

Investigate a documented network provider/event version with validated
per-event identity, or another supported method with independently verified
PID/process-epoch correlation. Compare event timestamps with process
start/stop, validate loss/latency and packet-size semantics, then conduct
manual Windows UAC and IPC threat-model tests before proceeding.

Keep the research isolated from first-party native plugin registration,
privileged collectors, release packaging and UI until the blocker is resolved.
No mock, TCP connection count, ETW header-PID assumption, or estimated packet
bytes may stand in for an approved live source.
