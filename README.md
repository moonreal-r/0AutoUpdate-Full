# 0AutoUpdate-Full

一个面向桌面的软件 上传-下载-自动更新 示例/完整实现（C#）。（项目结构与细节请参见源代码）。

---

## 功能实现

- UpdateServer 上传文件API
- AutoUpdateClient 目录构建-软件下载
- AutoUpdater 软件更新
- UpdaterReplacer 软件更新程序自我更新文件替换程序

---

## 技术选型

- 语言：C#（.NET 8）

---

## 工作流程

前提:安装.net 8 desktop runtime

1.开启UpdateServer

2.打包需上传文件(不需要打包目录,需包含版本信息local_version.txt)-通过API POST上传压缩包 参数:file,name(与启动文件exe同名),version

必须上传:AutoUpdateClient,AutoUpdater(AutoUpdater与UpdaterReplacer合并打包)

3.将AutoUpdateClient提供给客机

4.客机打开AutoUpdateClient.exe

5.选择需要的软件安装

6.启动对应软件目录下的AutoUpdate.exe

---
