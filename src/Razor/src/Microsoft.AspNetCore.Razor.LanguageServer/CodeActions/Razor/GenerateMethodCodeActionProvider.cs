﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.Language.Syntax;
using Microsoft.AspNetCore.Razor.LanguageServer.CodeActions.Models;
using Microsoft.CodeAnalysis;
using SyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using SyntaxNode = Microsoft.AspNetCore.Razor.Language.Syntax.SyntaxNode;

namespace Microsoft.AspNetCore.Razor.LanguageServer.CodeActions.Razor;

internal class GenerateMethodCodeActionProvider : IRazorCodeActionProvider
{
    private static readonly Task<IReadOnlyList<RazorVSInternalCodeAction>?> s_emptyResult = Task.FromResult<IReadOnlyList<RazorVSInternalCodeAction>?>(null);

    public Task<IReadOnlyList<RazorVSInternalCodeAction>?> ProvideAsync(RazorCodeActionContext context, CancellationToken cancellationToken)
    {
        var nameNotExistDiagnostics = context.Request.Context.Diagnostics.Where(d => d.Code == "CS0103");
        if (!nameNotExistDiagnostics.Any())
        {
            return s_emptyResult;
        }

        var change = new SourceChange(context.Location.AbsoluteIndex, length: 0, newText: string.Empty);
        var syntaxTree = context.CodeDocument.GetSyntaxTree();
        var owner = syntaxTree.Root.LocateOwner(change);
        if (owner is null)
        {
            return s_emptyResult;
        }

        if (IsGenerateEventHandlerValid(owner, out var methodAndEvent))
        {
            var uri = context.Request.TextDocument.Uri;
            var methodName = methodAndEvent.Value.methodName;
            var eventName = methodAndEvent.Value.eventName;
            var codeActions = new List<RazorVSInternalCodeAction>()
            {
                RazorCodeActionFactory.CreateGenerateMethod(uri, methodName, eventName),
                RazorCodeActionFactory.CreateAsyncGenerateMethod(uri, methodName, eventName)
            };
            return Task.FromResult<IReadOnlyList<RazorVSInternalCodeAction>?>(codeActions);
        }

        return s_emptyResult;
    }

    private static bool IsGenerateEventHandlerValid(
        SyntaxNode owner,
        [NotNullWhen(true)] out (string methodName, string eventName)? methodAndEvent)
    {
        methodAndEvent = null;

        // The owner should have a SyntaxKind of CSharpExpressionLiteral or MarkupTextLiteral.
        // MarkupTextLiteral if the cursor is directly before the first letter of the method name.
        // CSharpExpressionalLiteral if cursor is anywhere else in the method name.
        if (owner.Kind != SyntaxKind.CSharpExpressionLiteral && owner.Kind != SyntaxKind.MarkupTextLiteral)
        {
            return false;
        }

        // We want to get MarkupTagHelperDirectiveAttribute since this has information about the event name.
        // Hierarchy:
        // MarkupTagHelperDirectiveAttribute > MarkupTextLiteral
        // or
        // MarkupTagHelperDirectiveAttribute > MarkupTagHelperAttributeValue > CSharpExpressionLiteral
        var commonParent = owner.Kind == SyntaxKind.CSharpExpressionLiteral ? owner.Parent.Parent : owner.Parent;
        if (commonParent is not MarkupTagHelperDirectiveAttributeSyntax markupTagHelperDirectiveAttribute)
        {
            return false;
        }

        var eventName = markupTagHelperDirectiveAttribute.TagHelperAttributeInfo.Name[1..];
        if (markupTagHelperDirectiveAttribute.TagHelperAttributeInfo.ParameterName is { } parameterName
            && eventName.Contains(parameterName))
        {
            // An event parameter is being set instead of the event handler e.g.
            // <button @onclick:preventDefault=SomeValue/>, this is not a generate event handler scenario.
            return false;
        }

        var methodName = markupTagHelperDirectiveAttribute.Value.GetContent();
        if (!SyntaxFacts.IsValidIdentifier(methodName))
        {
            return false;
        }

        methodAndEvent = (methodName, eventName);
        return true;
    }
}
