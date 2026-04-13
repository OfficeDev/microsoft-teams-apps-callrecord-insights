# Contributing to Call Record Insights

Thank you for your interest in contributing to this project! This document provides
guidelines and instructions for contributing.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json` for the exact version)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) with Bicep (`az bicep install`)
- A code editor (Visual Studio, VS Code, or Rider recommended)

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a feature branch from `main`

## Building

```shell
dotnet build CallRecordInsights.sln
```

## Running Tests

```shell
dotnet test CallRecordInsights.sln
```

## Validating Bicep Templates

The Bicep templates can be validated locally without an Azure subscription:

```shell
# Lint all Bicep files
az bicep build --file deploy/bicep/deploy.bicep --stdout > /dev/null
```

## Regenerating the ARM Template

The ARM template at `deploy/resourcemanager/template.json` is generated from the Bicep
source. If you modify any Bicep files under `deploy/bicep/`, regenerate it:

```shell
az bicep build --file deploy/bicep/deploy.bicep --outfile deploy/resourcemanager/template.json
```

CI will fail if the ARM template is out of sync with the Bicep source.

## Deployment Testing

This repository is a **template** — it is not deployed to a central environment. If you
need to test deployment changes, you will need your own Azure subscription and M365 tenant.

See the [README](README.md) for deployment instructions using `deploy/deploy.ps1`.

## Submitting Changes

1. Ensure your changes build without errors: `dotnet build`
2. Ensure all tests pass: `dotnet test`
3. If you changed Bicep files, regenerate the ARM template (see above)
4. Push your branch and open a pull request against `main`
5. Fill out the PR template

## Versioning

The project version is defined in `Directory.Build.props` at the repo root. The release
workflow automatically creates a GitHub Release and bumps the patch version on every merge
to `main`.

Major and minor version bumps are **restricted to code owners only**. A CI check will
reject PRs from other contributors that modify the version. If you believe a version bump
is needed, note it in your PR and a code owner will handle it.

## Code Style

This project uses an `.editorconfig` (located in `src/`) to enforce code style. Most
editors will pick this up automatically. Please follow the existing patterns in the codebase.

## Code of Conduct

This project has adopted the [Microsoft Open Source Code of Conduct](CODE_OF_CONDUCT.md).
