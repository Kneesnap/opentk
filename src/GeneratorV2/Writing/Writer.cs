﻿using GeneratorV2.Parsing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace GeneratorV2.Writing
{
    public class Writer : Node
    {
        private readonly Specification _spec;
        
        private readonly string _path;

        public Writer(Specification spec) : base(new List<Node>())
        {
            _spec = spec;
            _path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw NullReferenceException(),
                "..", "..", "..", "..", "OpenToolkit.Graphics");

            const string namespaceName = "OpenToolkit.Graphics";
            foreach (var (name, api) in _spec.Apis)
            {
                AddNode(new ApiNode(_path, namespaceName, api));
            }

            AddNode(new FileNode(Path.Combine(_path, "Types.cs")))
                .AddNode(new NamespaceNode($"{namespaceName}.Types"))
                    .AddNode(new TypesNode());

            AddNode(new FileNode(Path.Combine(_path, "OpenToolkit.Graphics.csproj")))
                .AddNode(new ProjectNode(namespaceName));
        }

        private Exception NullReferenceException()
        {
            throw new NotImplementedException();
        }

        public override void Write()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, true);
            }
            Directory.CreateDirectory(_path);
            WriteChildren();
        }
    }
}
