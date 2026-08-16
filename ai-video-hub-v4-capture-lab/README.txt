AI Video Hub V4 原版成功轨迹采集器

用途：
只读记录“无限SD20更新版 / DoubaoAccountManager.exe”里 WebView2 真正成功生成视频时的网络和任务工作流，用于 V4 行为级重构。

使用前：
1. 关闭原版软件的所有窗口。
2. 双击 AI.VideoHub.V4.CaptureLab.exe。
3. 把原版文件夹里的 DoubaoAccountManager.exe 拖进黑色窗口，按 Enter。
4. 采集器会启动原版。正常登录/使用原版，不需要把密码提供给任何人。
5. 在原版里依次完成：
   - 一条原版本来就能成功的普通视频；
   - 如果原版支持15秒/30秒，再各成功生成一次；
   - 对成功视频执行原版的保存、无水印或原片操作。
6. 回到采集器窗口按 Enter。
7. captures 文件夹会生成 yyyyMMdd_HHmmss.zip，把这个 ZIP 发回 ChatGPT 即可。

隐私与安全：
- 不修改原版程序，不绕过许可证/账号权限。
- 调试端口只绑定 127.0.0.1（本机）。
- 不保存 Cookie、Authorization、Token、Password、Session、Signature 等敏感值原文。
- 敏感字段只保留字段名、长度和不可逆 SHA-256 指纹，以便比较字段是否动态变化。
- 采集结果只写在本机，不会自动上传。

注意：
必须通过采集器启动原版；直接先打开原版再开采集器，WebView2 不会继承临时调试参数。
