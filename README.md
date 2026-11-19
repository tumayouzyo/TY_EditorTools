# TY_EditorTools

Unity Editor用の小さな便利ツール集。Unity 2022.3以降。

## インストール

[VPM](https://tumayouzyo.github.io/vpm-repository/) から追加。

## Animator参照切れを検査・修復

ProjectでControllerを右クリック → `TY_EditorTools → Animator参照切れを検査・修復`。

参照元と遷移先の両方がない遷移データを削除します。使用中の遷移は変更しません。バックアップは `Library/AnimatorRepairBackups` に保存します。
