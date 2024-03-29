
# Uniscon

## Structure Concurrency for Unity Game Engine

Uniscon brings the concepts of [Structured Concurrency](https://en.wikipedia.org/wiki/Structured_concurrency) to Unity.  The name is a portmanteau of this (**UN**Ity **S**tructured **CON**currency).

This programming paradigm was first popularized by the python library [Trio](https://github.com/python-trio/trio) and Uniscon attempts to mirror these concepts in C#

If you aren't familiar with Trio - then in short, Structured Concurrency makes asynchronous tasks an order of magnitude easier to manage.  It achieves this by making the structure of code match the hierarchical structure of the async operations, which results in many benefits.

Installation
---

1. The package is available on the [openupm registry](https://openupm.com). It's recommended to install it via [openupm-cli](https://github.com/openupm/openupm-cli).
2. Execute the openum command.
    - ```
      openupm add com.svermeulen.uniscon
      ```

Usage
---

For usage see the tests at `TestTaskNursery.cs`.

Notes
---

* Not thread safe.  Assumption is that you're using it from unity main thread.  Currently unclear how this will be handled in the future when we want multi thread support

