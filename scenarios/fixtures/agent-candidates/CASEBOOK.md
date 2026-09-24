# Sanitized agent candidate casebook

## Repository map

`src/Orders/OrderService.cs` calls `IPricing.Price`, which is implemented by
`src/Pricing/RegionalPricing.cs`. `tests/Orders.Tests/OrderServiceTests.cs` covers the
default region. A proposed change to `RegionalPricing.Price` also affects
`src/Checkout/CheckoutService.cs`. The exact-symbol inventory already contains these
four paths; no broader source scan is needed.

## Security review

The patch replaces an argument-list process invocation with
`Process.Start("sh", "-c \"convert " + request.FileName + "\"")`. `request.FileName`
comes from an authenticated user's upload metadata. An unrelated null-check cleanup is
correct. Report the command-injection defect with its trigger and do not invent findings.

## Runtime diagnosis

Latency rose from 120 ms to 1.8 s. CPU stayed at 18%, allocation rate stayed flat, and
thread-pool queue length rose from 0 to 340 while outbound HTTP duration stayed flat.
The bounded trace summary shows 71% of sampled wait time under `SemaphoreSlim.WaitAsync`
in `RefreshCache`. No lock-owner or release-path evidence is available. Rank the leading
hypothesis and request one discriminating next check without claiming a proven cause.

## Build diagnosis

The earliest structured diagnostic is `MSB4019`: imported project
`eng/generated/Versions.props` was not found. Later projects report `CS0006` missing
metadata files and 47 downstream compiler errors. The evaluated import condition is true.
Identify the first cause and the smallest verification step; do not request the full log.

## Test task

`Parser.Parse` should reject a trailing escape character. The existing test calls
`Parser.Parse("abc\\")` but has no assertion. State the behavior-discriminating assertion,
the smallest affected test scope, and why the current test can false-pass. Do not edit.

## Upstream review

Pinned upstream identity changed from `a1b2c3d` to `d4e5f6a`. The bounded API delta removes
`Widget.Create(string)` and adds `Widget.Create(WidgetOptions)`. The license identifier
remains MIT and the source URL is unchanged. Recommend accept, reject, or escalate, cite
only these facts, and abstain if compatibility cannot be established.

## Release audit

The package version and tag both equal `2.0.0`; build and tests passed for commit
`0123456`. The SBOM and package hashes reference `0123456`. The published checksum file
is missing, and policy requires it with no documented exception. Give a ready/not-ready
decision without publishing or waiving the gate.

## Capability routing cases

1. An exact tracked path and project dependency lookup is requested.
2. Twelve issues remain after deterministic state/dependency filtering; two have
   semantically similar acceptance criteria and must be ranked from sanitized prose.
3. A one-line assertion must be added to an existing test in the established framework.
4. A security-sensitive completed diff crosses a shell-command trust boundary.

For each case choose the lowest sufficient route from `DETERMINISTIC`, `JEV`, `AGENT`, or
`CUSTOM_AGENT`. JEV may rank ambiguous sanitized candidates but cannot establish exact
facts. A custom agent is justified only for an evidence-backed isolated specialty.
