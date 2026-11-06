# ClaerPlayer

<!-- impeccable:product-schema 1 -->

## Platform
Windows desktop, x64; Windows 10/11 and .NET Framework 4.8.

## Users and purpose
The user wants an installable Windows player with broad video-format support, animated GIF playback, low overhead, and faithful picture reproduction.

## Confirmed constraints
Dark viewing surface with simple controls. Preserve aspect ratio and source detail. Do not claim every format or an improvement beyond the source. Avoid a browser runtime and separately installed codec packs.

## Implementation decisions
C# WinForms and libmpv, chosen to reuse the Windows runtime and keep the application shell small. Korean UI. Per-user installer plus portable package. ClaerPlayer is the exact product name chosen by the user, including its spelling.

## Validation scope
Use generated media for codec, container, GIF, playback controls and error handling tests. HDR display accuracy and low-end hardware performance require separate device validation.
