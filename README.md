# Vial

A modding toolkit for the Unity version of Town of Salem.

### Terms of Use

By using Vial and its source code, you agree to do nothing malicious using it, and to do nothing with malicious intent using it.
None of the developers of Vial are responsible for the use of Vial.

### Installation Instructions

Currently, there is no user-friendly way of installing Vial.
If you don't know how to clone the repository and build the project yourself, then Vial won't *yet* be of any use to you.

### Solution Structure

* `Vial` contains the modifications injected into `Assembly-CSharp`.
* `Vial.Mixin` contains the attributes used to instruct the installer how to inject the modifications.
* `Vial.Analyzer` contains the Roslyn code analyzers used to verify the proper use of the attributes in `Vial.Mixin`.
* `Vial.Installer` contains the system that injects the modifications into the assembly.
* `dnlib` is a dependency used by `Vial.Installer` to read & write assemblies.

### Objectives

* Making modding more accessible and eventually removing the need to reverse-engineer the game in order to create increasingly complex mods.
* Improving understanding of the game's structure & logic.
* Encouraging BlankMediaGames (the developers of Town of Salem) to follow proper cheat-proof procedures, within reason, when maintaining their game.
This means, for example, not sending all the players' roles at the start of the game (credit to them, they already do this).
* Helping BlankMediaGames identify certain bugs.

### To Do List

* Make `Vial.Installer` more future-proof.
* Create a mod loader so that simple mods don't need to be injected.
* Create an API to minimize incompatibilities between various mods' desired changes.
