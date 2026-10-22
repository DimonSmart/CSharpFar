# Network Traffic ETW feasibility — blocking Windows spike

Status: **UNVERIFIED / No Go for product integration** (2026-10-22).

This is an experiment, not a Network Traffic plugin or proof of correctness.

## Hypothesis

A private uniquely named real-time SystemTraceProvider session, enabled through
TraceEvent \`EnableKernelProvider(NetworkTCPIP | Process)\`, supplies TCP/UDP
Send/Receive events with trustworthy payload PID, size, and IPv4/IPv6 metadata,
without taking over the existing NT Kernel Logger. The event payload's size
semantics and real delivery lag must be measured before a collector is designed.

## Reproduction

Windows 10/11 x64 with .NET 10 SDK:

\`\`\`powershell
dotnet run --project tools/NetworkTraffic.EtwSpike -- --output etw-spike-result.json
\`\`\`

Run *both* as a standard user and as an elevated user. The probe never elevates
itself and never touches any other named trace session. It creates loopback
TCP/UDP IPv4/IPv6 traffic, waits for ETW buffers, and records counts, payload
PIDs and sizes, event names, lost events and approximate delivery lag. All
generated traffic is local; no packet contents or endpoint addresses are logged.
\`EventsLost\` is source-side loss only and may not cover every logger loss metric.

The dedicated Windows GitHub Actions feasibility workflow runs the experiment
independently of the application's cross-platform build and uploads \`etw-spike-result.json\` as evidence. Success on a hosted CI image does
not establish success for an unprivileged local user, UAC flow, or portable ZIP.

## Go / No-Go criteria

Do not proceed to the privileged collector and UI before **real Windows evidence**
confirms all of:

- Separate system logger session creation; no interference with foreign sessions
- Four supported event families (TCP/UDP x Send/Receive) and IPv4/IPv6
- PID from payload and size semantics, including a short-lived process and PID reuse
- Lag and loss metrics, high-rate behavior and interrupted/crashed session cleanup
- Actual privilege requirements, UAC, non-administrator and alternate-SID cases
- Practical comparison with other Windows counters; no false promise of
  identical bytes or classification as internet traffic

Merely compiling the spike or observing some loopback events is **not Go**.
A successful final decision must record tested Windows versions, counts,
latency/loss samples, known limits and the corresponding recommendation.

## Current result

The authoring environment is Linux without .NET SDK or access to a Windows
trace session. A Windows GitHub Actions runner (Windows build 26100) initially failed with
`The kernel provider must be enabled first and only once in a session`.
TraceEvent source inspection showed the probe accessed `session.Source`
before enabling the kernel provider. This ordering has been corrected.
That first failure was a probe bug, not an ETW capability verdict.

No verified live TCP/UDP events, UAC behavior or loss metrics are yet recorded.
A successful automated observation would still require separate manual
Windows validation of privileges, PID epochs, UAC and private-session cleanup.
Pending the full evidence, product integration remains blocked.
