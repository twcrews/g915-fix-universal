# AGENTS.md

## Project Context

This repository is a fork. The goal of this fork is to add cross-platform functionality across Windows, macOS, and Linux.

The original project can be browsed at the public GitHub repo called `lucduguaysita/G915-Stutter-Fix`. Local git history can also be referenced, although the original code has been removed in the latest commits.

## Guidance for Agents

- Preserve existing behavior unless a task explicitly requires changing it.
- Prefer changes that move the project toward platform-agnostic design.
- When adding or modifying functionality, consider Windows, macOS, and Linux compatibility.
- Avoid introducing platform-specific assumptions unless they are isolated behind clear abstractions.
- Update documentation when behavior or platform support changes.
