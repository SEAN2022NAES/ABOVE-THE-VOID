---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 6491cd71091331ad1a3a0da9eb56ff2c_9893a026dd6911f19280525400393706
    ReservedCode1: rEjyDd9TY+pIkgk23BhdXVE8CyiZyNNgraCeIm9jDNP8iohDU/oPtS6Sv2mEitlbk3rrDWvkK+xawrI5JpzLSk8mRs9K4Hw8sRFHrQiGyqE4TSdHPYJuLT99CUbj6XuXsZCLSmm1bOj0DECSkY0YvYFPFmg4pgIkRQBa4riulOkN+CU+ipwLhd8n+hI=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 6491cd71091331ad1a3a0da9eb56ff2c_9893a026dd6911f19280525400393706
    ReservedCode2: rEjyDd9TY+pIkgk23BhdXVE8CyiZyNNgraCeIm9jDNP8iohDU/oPtS6Sv2mEitlbk3rrDWvkK+xawrI5JpzLSk8mRs9K4Hw8sRFHrQiGyqE4TSdHPYJuLT99CUbj6XuXsZCLSmm1bOj0DECSkY0YvYFPFmg4pgIkRQBa4riulOkN+CU+ipwLhd8n+hI=
---

# 仓库改造清单 · MIT → 自定义双许可 / Repository Migration Checklist: MIT → Custom Dual Licence

Copyright (c) 2026 Ma Yixiao (Sean Ma). All rights reserved.

适用范围：**ABOVE THE VOID**（`https://github.com/SEAN2022NAES/ABOVE-THE-VOID`），从 GNU 风格的宽松 MIT 协议
切换为「非商业免费 + 商业需书面授权」的自定义双许可。

Scope: switching **ABOVE THE VOID** from MIT to a custom dual licence (free non-commercial use + written
commercial licence only).

> ⚠️ 法律要点 / Legal note
> 许可变更**只能约束变更生效之日以后的使用行为**。变更前已经按 MIT 获得的副本，其授权不可撤回；
> 换言之，任何在变更日之前拿到代码的人，仍可依 MIT 继续使用该版本。因此**版本节点与仓库公告**必须写清楚。
> A licence change **only binds use from the effective date onward**. Copies obtained under MIT before that date
> remain licensed under MIT and that grant cannot be revoked. The version boundary must therefore be stated
> clearly in the repository.

---

## 0. 变更前确认 / Before You Start

- [ ] 确认你是**全部著作权的唯一权利人**：代码、美术、音效、字体、Shader、第三方素材是否全部自研或已获授权。
- [ ] 逐一核查第三方资源许可：字体（如微软雅黑、Consolas 不可随包分发商用）、音效库、免费素材站的授权条款，
      是否存在与本许可冲突或要求单独标注的部分。
- [ ] 确认仓库协作历史：是否有他人提交（外部 PR / 协作者 commit）。若有，变更许可前需取得其书面同意，
      否则该部分代码不能纳入新许可。
- [ ] 记录变更基线：`git log -1 --format="%H %ai"` 留存当前 HEAD 哈希与时间，作为「MIT 期终止点」。

---

## 1. 需要删除 / 替换的文件 / Files to Remove or Replace

| 状态 | 文件 | 动作 |
|---|---|---|
| **替换** | `LICENSE`（原 MIT 正文） | 已替换为自定义双许可全文（含 All Rights Reserved 声明、非商业许可范围、商业用途清单、署名要求、终止条款、免责声明、解释与变更权）。/ Replaced with the full custom dual-licence text. |
| **新增** | `LICENSE-COMMERCIAL.md` | 商业授权说明（不含具体报价）。/ Commercial licensing explainer, no price list. |
| **新增** | `docs/COMMERCIAL-LICENSE-INQUIRY.md` | 商业授权询问 / 回复 / 交涉邮件模板（中英）。/ Enquiry, reply and enforcement e-mail templates. |
| **新增** | `docs/LICENSE-MIGRATION-CHECKLIST.md` | 本清单。/ This checklist. |
| **核查** | `.gitignore` | 确认未把 `LICENSE` 等法律文件纳入忽略规则。/ Ensure legal files are not ignored. |
| **核查** | GitHub 仓库设置 → License 识别 | GitHub 可能仍显示 MIT（缓存 / 识别延迟）；确认根目录只有一份 `LICENSE`，无 `LICENSE.md`、`COPYING`、`LICENSE.txt` 并存导致识别混乱。/ GitHub may still show MIT from cache; make sure only one `LICENSE` exists. |
| **核查** | 项目内其余许可副本 | 搜索仓库内是否还有 `LICENSE-MIT`、`COPYING`、`Assets/**/LICENSE.txt`、第三方包 `Packages/**` 中的许可文件 —— 第三方包的许可是它们自己的，**不要改动**。/ Search for stray licence copies; do not touch third-party package licences under `Packages/`. |

**自查命令 / Verification commands**

```bash
# 查仓库内所有疑似许可文件
git ls-files | grep -iE '(^|/)(licen[cs]e|copying|notice)' 
# 查代码与文档中残留的 MIT 表述
git grep -niE 'MIT licen|MIT 协议|MIT License'
# 确认根目录只有一份 LICENSE
ls -1 LICENSE* 2>/dev/null
```

---

## 2. 需要补声明的位置 / Where to Add Notices

### 2.1 仓库根目录（已完成 / Done）

- [x] `LICENSE` — 双许可全文，首行即 `Copyright (c) 2026 Ma Yixiao (Sean Ma). All rights reserved.`
- [x] `LICENSE-COMMERCIAL.md` — 商业授权流程与法律后果
- [x] `README.md` — 新增「许可 / License」章节（见 §2.2）

### 2.2 README 章节 / README Section

- [ ] 在 `README.md` **正文靠前位置**（Features 之前或 Requirements 之后）插入「许可」章节，
      让访客 30 秒内明白「学习免费、商用要谈」。
- [ ] 章节内必须包含：一句话结论 + 允许/禁止要点 + 指向 `LICENSE` 与 `LICENSE-COMMERCIAL.md` 的链接 +
      版权行 `© 2026 Ma Yixiao (Sean Ma). All rights reserved.`
- [ ] 若 README 顶部有徽章（badge）区，插入许可徽章替换原 MIT 徽章，例如：
      `![License](https://img.shields.io/badge/license-Non--Commercial%20%2B%20Commercial%20Licence-blue)`
- [ ] 版本日志中新增对应版本条目，写明「许可变更为双许可」，并注明变更前版本仍为 MIT。

### 2.3 源码文件头 / Source File Headers

建议在全部 `.cs`（当前 35 个）文件头部统一加一段 3–4 行注释。**建议格式**（英文短、中文短，不改动原有
`/// <summary>` 结构）：

```csharp
// ABOVE THE VOID — Copyright (c) 2026 Ma Yixiao (Sean Ma). All rights reserved.
// Non-commercial use only. Commercial use requires a prior written licence.
// https://github.com/SEAN2022NAES/ABOVE-THE-VOID/blob/main/LICENSE-COMMERCIAL.md
```

- [ ] 优先覆盖**核心自研模块**：`CoordinateSystem*.cs`、`Camera*.cs`、`BlockSystem*.cs`、`CodeTower*.cs`、
      `CodeEditorController.cs`、`CodeSyntaxTutorialController.cs`、`CodeIntroTutorialController.cs`、
      `BlockIntroController.cs`、`LevelDirector.cs`、`LevelProgress.cs`、`GameProgress.cs`、`MainMenuController.cs`
- [ ] 批量加头脚本建议先在小范围（3 个文件）验证编译无误后再全量执行；
      注意保留原文件 BOM / 换行风格，避免产生整文件 diff 噪音。
- [ ] Shader（`.shader` / `.compute`）文件同步加 3 行头注释。

> 提示：加头注释会改动大量文件，建议**单独一个 commit**（`Docs: add licence headers to source files`），
> 便于后续 `git blame` 与回溯。

### 2.4 Unity 编辑器 About 面板 / Unity Editor About Panel

- [ ] `ProjectSettings/ProjectSettings.asset` → `companyName` 建议改为 `Ma Yixiao (Sean Ma)`，
      `productName` 为 `ABOVE THE VOID`，使构建产物的元信息与版权一致。
- [ ] 如需在编辑器菜单加「关于」项，可新增 `Assets/Editor/AboutWindow.cs`，内容包含：
      项目名、作者、版权行、许可摘要、商业授权链接。
- [ ] 确认 `PlayerSettings` 中的 `Copyright`（部分平台构建会写入可执行文件元数据）填写为
      `Copyright (c) 2026 Ma Yixiao (Sean Ma). All rights reserved.`

### 2.5 游戏内版权页 / In-Game Credits

- [ ] `Assets/Scripts/MainMenuController.cs` 第 526–529 行（`authorText` 赋值处）建议改为：

      zh: "作者  ·  Ma Yixiao (Sean Ma)        © 2026 保留所有权利  ·  仅限非商业使用"
      en: "Author  ·  Ma Yixiao (Sean Ma)        © 2026 All Rights Reserved  ·  Non-Commercial Use Only"

- [ ] 主菜单底部或「项目目的」面板下方，追加一行小字，提示商业授权需联系作者（可只放仓库链接，避免暴露邮箱）。
- [ ] 若存在独立的 Credits / 致谢界面，加入完整版权段：

      ABOVE THE VOID
      © 2026 Ma Yixiao (Sean Ma). All rights reserved.
      Licensed for non-commercial use only.
      Commercial licensing: https://github.com/SEAN2022NAES/ABOVE-THE-VOID/blob/main/LICENSE-COMMERCIAL.md

- [ ] 启动画面（Splash）如可自定义，确认未移除版权信息；Unity Personal 的 Splash 不可关闭，无需处理。

### 2.6 构建产物 / Build Outputs

- [ ] 游戏内 `StreamingAssets` 或安装目录中放置 `LICENSE.txt` 与 `LICENSE-COMMERCIAL.txt`
      （供玩家与审查方查看），并在 About 界面给出相对路径。
- [ ] 若上架商店，商店页面的「版权信息」字段填写 `© 2026 Ma Yixiao (Sean Ma)`。

### 2.7 对外渠道 / External Channels

- [ ] GitHub 仓库首页 About 区：描述末尾加 `Non-commercial use free with attribution · Commercial use requires a licence`。
- [ ] 若有 itch.io / B 站 / 知乎 / 公众号等项目页，同步更新许可说明与版权行。
- [ ] 若曾对外宣称「本项目为 MIT 开源」，需在这些渠道发布变更说明，避免被主张「虚假陈述」或「禁反言」。

---

## 3. 权属留证动作 / Evidence Preservation

| 动作 | 具体做法 | 目的 |
|---|---|---|
| **Git 历史保留** | 不 force-push、不重写历史、不删仓库；`git tag` 为每个版本打标签；定期 `git bundle create backup.bundle --all` 打包全量历史 | 证明创作时间与创作过程，是软件著作权归属的核心证据 |
| **版本快照归档** | 每个 release 打 tag 后，导出该版本的源码 zip（GitHub Release 附件或本地归档），记录 SHA-256 | 证明特定版本的完整内容 |
| **软著登记** | 在中国版权保护中心提交计算机软件著作权登记：源程序前后各连续 30 页（不足 60 页全交）、文档 60 页、申请表、身份证明 | 取得登记证书，作为权属的初步证明，便于行政投诉与诉讼立案 |
| **源文件冷备** | 源码与资源双备份：一块离线硬盘 + 一份异地/云冷存储；每季度校验一次可读性 | 防丢失、防篡改，证明在先创作 |
| **时间戳 / 存证** | 对关键版本做可信时间戳（TSA）或区块链存证，固化「某时刻已存在该内容」 | 强化证据效力，成本低 |
| **创作过程留痕** | 保留设计文档、原型、录屏、提交信息（commit message 写清功能与日期） | 证明独立创作，反驳「抄袭」抗辩 |
| **对外授权留痕** | 所有商业授权以书面（邮件 + 签署件）形式，编号归档，记录范围/期限/费用 | 证明授权边界，避免被扩大解释 |
| **侵权取证** | 发现未授权使用时，用可信时间戳或公证方式固定网页、商店页、下载包哈希 | 保证证据在诉讼中可用 |

**建议节奏 / Suggested cadence**

- 每个 release：tag + 源码归档 + 哈希记录
- 每季度：全量 `git bundle` + 冷备校验
- 首版商用前：完成软著登记（普通程序审查约 30–60 个工作日，可加急）

---

## 4. 提交与发布步骤 / Commit and Release Steps

```bash
# 1) 落地法律文件（建议与代码改动分开提交）
git add LICENSE LICENSE-COMMERCIAL.md docs/COMMERCIAL-LICENSE-INQUIRY.md docs/LICENSE-MIGRATION-CHECKLIST.md README.md
git commit -m "License: switch from MIT to custom dual licence (non-commercial free, commercial by written licence)"

# 2) 源码文件头（单独一次提交，避免混合 diff）
git add Assets/Scripts/*.cs
git commit -m "Docs: add copyright and licence headers to source files"

# 3) 记录变更基线
git log -1 --format="%H %ai"

# 4) 打标签并推送
git tag -a v0.2.3 -m "v0.2.3 — dual licence"
git push origin main
git push origin v0.2.3
```

- [ ] 推送后在 GitHub 仓库 **Releases** 页面为 `v0.2.3` 撰写说明，明确写：
      「自 v0.2.3 起本仓库改为双许可；v0.2.2 及更早版本仍按 MIT 提供。」
- [ ] 在 GitHub 仓库 **Insights → Community Standards** 确认许可文件被识别为新许可（自定义许可会显示
      "View license"，属正常）。
- [ ] 检查 GitHub 仓库首页右侧是否仍显示 "MIT license" 徽章 —— 若仍显示，多为缓存，24 小时后复查；
      仍不更新则确认无其他 `LICENSE` 文件残留。

---

## 5. 回归验证 / Verification

- [ ] `git grep -niE 'MIT licen|MIT 协议'` 无残留（历史版本说明中的「曾用 MIT」属正常，需保留）。
- [ ] 仓库根目录 `LICENSE` 首屏可见 `All Rights Reserved` 与「商业用途需书面授权」。
- [ ] `README.md` 许可章节中的两个链接可点击跳转成功。
- [ ] 游戏构建产物启动后，主菜单能看到 `© 2026 Ma Yixiao (Sean Ma). All rights reserved.`
- [ ] `LICENSE-COMMERCIAL.md` 中 `[联系方式占位符]` 已替换为真实可收信邮箱（发布前必须替换）。
- [ ] 所有对外渠道的许可表述一致（避免一处写 MIT、一处写双许可）。

---

## 6. 常见坑 / Common Pitfalls

1. **只改 LICENSE 文件是不够的**：README、源码头、游戏内版权页、商店页面不一致时，对方可主张「合理信赖」，
   削弱维权立场。
2. **旧版本授权不可撤回**：v0.2.2 及更早版本的 MIT 授权永久有效，无法收回。若某版本已广泛传播，
   唯一补救是**向前收口**：新版本换许可 + 新功能只在许可版本中提供。
3. **协作者代码**：任何外部贡献者提交的代码，其著作权归其本人，改许可前需取得书面同意（或该部分单独保留 MIT）。
4. **字体与素材**：Unity 默认字体、系统字体（微软雅黑 / Consolas）不可随包对外分发；免费素材站素材常有
   「不可商用」「需署名」条款，需与其许可对齐，否则你会因第三方资源被追责。
5. **软著登记与开源披露**：软著登记要求提交源程序，若代码已公开，登记时仍可提交（公开不影响登记），
   但登记表中的「发表状态」应如实填写「已发表」。
6. **邮箱占位符**：发布前必须替换 `[联系方式占位符]`，否则潜在合作方无法联系，双许可的商业侧形同虚设。
*（内容由AI生成，仅供参考）*
