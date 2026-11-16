# Roadmap

Frosty Neon Studio is in beta. This page lists what is known to be missing, roughly in the order it is likely to be worked on. Nothing here has a date.

## Testing

The biggest gap is testing on more avatars. See [Tested with](TEST_AVATARS.md) for what has been done.

- Booth avatars from start to finish, including ones that use lilToon and Poiyomi.
- Quest versions looked at on a Quest headset.
- A person clicking through every dialog in the app and the Avatar Guide.
- The full setup on a second, clean PC.

If you try any of these, please [report how it went](../CONTRIBUTING.md#reporting-a-problem), even when it worked.

## Features being considered

- **VRM 1.0** VRoid files. This needs UniVRM's separate VRM 1.0 package, which registers its own importer for `.vrm` files.
- **Blinking and expressions.** Avatars set up by the toolkit have no blink, expressions menu or custom gestures. Shapes such as `vrc.blink_left` are not wired yet.
- **Bind pose check.** Warn about meshes whose bind poses are far from their bones.
- **PhysBones and other components** in the check, with Quest's limits.
- **Separate PC and Quest materials for plain models.** Today a plain model gets one set of mobile materials for both.

## Later

- **World tools.** The toolkit is avatar-first; world creation is a later phase.
- **Other systems.** The toolkit is Windows only.
