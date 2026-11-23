# Third-party notices

FairyToon does not ship a license for its own code. The copyright holder should choose one. This file covers third-party material only.

## lilToon

Some inspector wording and the shape of the material inspector are adapted from lilToon 2.4.1.

- Project: https://github.com/lilxyzw/lilToon
- Commit: e698a2f791bd28dd1aabf227c74f14bedeca3587
- Files used as reference: `Assets/lilToon/Editor/lilEnumeration.cs`, `lilLanguageManager.cs`, `lilEditorGUI.cs`, `lilInspector/lilInspector.cs`, `lilInspector/lilGUIUtility.cs`, `lilInspector/lilMainInspectorGUI.cs`, `lilInspector/lilSettingAndPresetGUI.cs`, `lilInspector/lilMaterialProperties.cs`, `lilInspector/lilPropertyGroupDrawerBaseSetting.cs`, `lilInspector/lilPropertyGroupDrawerColorSetting.cs`, `lilToonPropertyDrawer.cs`, `Editor/Localization/en-US.po`, `ja-JP.po`, `zh-Hans.po`, `Editor/Resources/gui_box_outer_*.png`, `gui_box_inner_*.png`, `gui_box_inner_half_*.png`, `gui_custom_box_*.png`

Adapted in this package:

- `Editor/FairyToonL10n.cs` and `Editor/FairyToonL10n.Catalog.cs` (editor-mode names, section titles, and other strings taken from the `.po` files)
- `Editor/FairyToonPropertyBlocks.cs` (property names, texture flags, and property-block membership from `lilMaterialProperties.cs`)
- `Editor/FairyToonClipboard.cs` (accumulating `CopiedProperties` / `CopiedVectorProperties`, Copy, Paste, Paste with texture, Reset)
- `Editor/FairyToonGUI.cs` (Advanced / Preset / Shader Setting toolbar, row search, Shuriken foldout header, parent and child gears, localized enum options, section order, and the outer/inner box layout from `lilEditorGUI.InitializeBox` and `InitializeGUIStyles`)

`Editor/Resources/gui_box_*.png` and `gui_custom_box_*.png` are the lilToon 2.4.1 GUI textures, copied under the MIT license above. The textures keep lilToon's 4 px and 6 px border slices. FairyToon then sets the outer box margin to (4, 4, 0, 4) and padding to (2, 2, 0, 0), and the inner half and full boxes to margin (2, 2, 0, 2). The header-to-box gap is a separate 2 pt space, not the box top margin. The untitled Base box uses `gui_custom_box`.

The package that ships the GUI textures uses this copyright line: `Copyright (c) 2020-present lilxyzw`.

The license text below is the root `LICENSE` file at that commit.

```
MIT License

Copyright (c) 2020-2024 lilxyzw

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
