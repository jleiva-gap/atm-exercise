# SPDX-License-Identifier: Apache-2.0
# Licensed to the Ed-Fi Alliance under one or more agreements.
# The Ed-Fi Alliance licenses this file to you under the Apache License, Version 2.0.
# See the LICENSE and NOTICES files in the project root for more information.

<#
.SYNOPSIS
Script to set up the development environment for the repository.

.DESCRIPTION
This script restores .NET tools and PowerShell resources, then installs Husky for managing Git hooks.
#>

Write-Information "Setting up the development environment..." -InformationAction Continue

# Restore .NET tools
Write-Information "Restoring .NET tools..." -InformationAction Continue
dotnet tool restore

# Install CSharpier
Write-Information "Installing CSharpier..." -InformationAction Continue
dotnet tool install --local csharpier

# Install Husky
Write-Information "Installing Husky..." -InformationAction Continue
dotnet husky install

Write-Information "Development environment setup complete." -InformationAction Continue
