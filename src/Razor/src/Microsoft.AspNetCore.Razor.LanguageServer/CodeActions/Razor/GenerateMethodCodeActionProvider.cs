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
using Microsoft.AspNetCore.Razor.LanguageServer.Common;
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

        if (IsGenerateEventHandlerValid(owner, context, out var @params))
        {
            return Task.FromResult<IReadOnlyList<RazorVSInternalCodeAction>?>(CreateCodeAction(@params));
        }

        return s_emptyResult;
    }

    private static List<RazorVSInternalCodeAction> CreateCodeAction(GenerateMethodCodeActionParams[] @params)
    {
        var result = new List<RazorVSInternalCodeAction>();
        foreach (var param in @params)
        {
            var resolutionParams = new RazorCodeActionResolutionParams()
            {
                Action = LanguageServerConstants.CodeActions.GenerateEventHandler,
                Language = LanguageServerConstants.CodeActions.Languages.Razor,
                Data = param,
            };

            var codeAction = param.IsAsync
                ? RazorCodeActionFactory.CreateAsyncGenerateMethod(resolutionParams)
                : RazorCodeActionFactory.CreateGenerateMethod(resolutionParams);
            result.Add(codeAction);
        }

        return result;
    }

    private static bool IsGenerateEventHandlerValid(
        SyntaxNode owner,
        RazorCodeActionContext context,
        [NotNullWhen(true)] out GenerateMethodCodeActionParams[]? @params)
    {
        @params = null;

        // The owner should have a SyntaxKind of CSharpExpressionLiteral or MarkupTextLiteral.
        // MarkupTextLiteral if the cursor is directly before the first letter of the method name.
        // CSharpExpressionalLiteral if cursor is anywhere else in the method name.
        if (owner.Kind != SyntaxKind.CSharpExpressionLiteral && owner.Kind != SyntaxKind.MarkupTextLiteral)
        {
            return false;
        }

        if (!TryParseMethodAndEventName(owner, out var methodName, out var eventName))
        {
            return false;
        }

        @params = new[]
        {
            new GenerateMethodCodeActionParams()
            {
                Uri = context.Request.TextDocument.Uri,
                MethodName = methodName,
                EventName = eventName,
                IsAsync = false,
            },
            new GenerateMethodCodeActionParams()
            {
                Uri = context.Request.TextDocument.Uri,
                MethodName = methodName,
                EventName = eventName,
                IsAsync = true,
            }
        };

        return true;
    }

    private static bool TryParseMethodAndEventName(
        SyntaxNode owner,
        [NotNullWhen(true)] out string? methodName,
        [NotNullWhen(true)] out string? eventName)
    {
        methodName = null;
        eventName = null;

        var commonParent = owner.Kind == SyntaxKind.CSharpExpressionLiteral ? owner.Parent.Parent : owner.Parent;
        if (commonParent.Kind != SyntaxKind.MarkupTagHelperDirectiveAttribute)
        {
            return false;
        }

        var children = commonParent.ChildNodes();
        var csharpExpressionLiteralNode = owner;

        if (csharpExpressionLiteralNode.Kind == SyntaxKind.MarkupTextLiteral)
        {
            // We have a MarkupTextLiteral and need to find the CSharpExpressionLiteral node.
            // The hierarchy here starting with the parent of the MarkupTextLiteral node is:
            // MarkupTagHelperDirectiveAttribute > MarkupTagHelperAttributeValue > CSharpExpressionLiteral
            var markupTagHelperAttributeValueNode = children.FirstOrDefault(c => c.Kind == SyntaxKind.MarkupTagHelperAttributeValue);
            csharpExpressionLiteralNode = markupTagHelperAttributeValueNode?.ChildNodes()
                .FirstOrDefault(c => c.Kind == SyntaxKind.CSharpExpressionLiteral);
            if (csharpExpressionLiteralNode is null)
            {
                return false;
            }
        }

        var content = csharpExpressionLiteralNode.GetContent();
        if (!SyntaxFacts.IsValidIdentifier(content))
        {
            return false;
        }

        methodName = content;

        for (var i = 1; i < children.Count; i++)
        {
            if (children[i].Kind == SyntaxKind.Equals)
            {
                // We assume that the node that contains the event name will always come before the one equals node.
                var node = children[i - 1];
                if (node.Kind != SyntaxKind.MarkupTextLiteral)
                {
                    return false;
                }

                eventName = node.GetContent();
                return true;
            }
        }

        return false;
    }
}
