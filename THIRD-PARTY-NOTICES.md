# Third-party notices

This plugin's compiled distribution (the release zip) includes the following third-party
components, both MIT-licensed, reproduced below with their original copyright notices. MIT-licensed
code is compatible with inclusion in this plugin's own GPL-3.0 license (see repo root `LICENSE`).

## TapoConnect

- **NuGet package**: [TapoConnect 3.2.4](https://www.nuget.org/packages/TapoConnect/3.2.4)
- **Project**: https://github.com/cwakefie27/TapoConnect
- **License**: MIT

```
MIT License

Copyright (c) 2024 cwakefie27

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Portable.BouncyCastle

- **NuGet package**: [Portable.BouncyCastle 1.8.9](https://www.nuget.org/packages/Portable.BouncyCastle/1.8.9) (a transitive dependency of TapoConnect, shipped as `BouncyCastle.Crypto.dll`)
- **Project**: https://www.bouncycastle.org/csharp/
- **License**: MIT (Bouncy Castle License)

```
MIT License (https://opensource.org/licenses/MIT)

Copyright (c) 2000-2021 The Legion of the Bouncy Castle Inc. (https://www.bouncycastle.org)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sub license, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions: The above copyright notice and this
permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Reference implementations (not bundled, consulted while writing this plugin's own code)

No code from either project below is compiled into this plugin - neither is a dependency. They are
credited here because `SmartPlugControlCloud/KasaCloudPassthroughClient.cs` was written with direct
reference to their source to understand the TP-Link cloud passthrough protocol (see that file's own
doc comment). This is the reason this plugin as a whole is licensed under GPL-3.0-only rather than a
permissive license - see the repo's `CLAUDE.md` for the full reasoning.

- **piekstra/tplink-cloud-api** - https://github.com/piekstra/tplink-cloud-api - License: GPL-3.0
  (their own `LICENSE` file doesn't fill in the "or later version" clause, so it's ambiguous which
  GPL-3.0 variant applies)
- **python-kasa** - https://github.com/python-kasa/python-kasa - License: GPL-3.0-or-later (their
  `LICENSE` explicitly includes "or (at your option) any later version")
