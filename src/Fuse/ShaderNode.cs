﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VL.Lib.Collections;


namespace Fuse
{
    public interface IShaderNode: Trees.IReadOnlyTreeNode
    {

        string Id { get;  }
    }

    public class FieldDeclaration
    {
        private readonly string _computeShaderDeclaration;
        private readonly string _declaration;

        public FieldDeclaration(string computeShaderDeclaration, string declaration)
        {
            _computeShaderDeclaration = computeShaderDeclaration;
            _declaration = declaration;
        }

        public FieldDeclaration(string declaration)
        {
            _computeShaderDeclaration = declaration;
            _declaration = declaration;
        }

        public string GetDeclaration(bool theIsComputeShader)
        {
            return theIsComputeShader && _computeShaderDeclaration != null ? _computeShaderDeclaration : _declaration;
        }
            
    }
    
    public abstract class AbstractShaderNode : IShaderNode
    {
        protected IEnumerable<AbstractGpuValue> Ins;

        protected abstract string SourceTemplate();

        public abstract AbstractGpuValue AbstractOutput();

        protected IDictionary<string, string> CustomTemplateValues;


        protected AbstractShaderNode(string theId)
        {
            Id = theId;
        }

        public virtual IDictionary<string,string> Functions => new Dictionary<string, string>();
        
        public string Id { get; }
        
        public IReadOnlyList<Trees.IReadOnlyTreeNode> Children
        {
            get
            {
                var result = new List<Trees.IReadOnlyTreeNode>();
                
                Ins.ForEach(input =>
                {
                    if(input?.ParentNode != null)result.Add(input.ParentNode);
                });
                return result;
            }
        }
        
        protected const string DefaultShaderCode = "${resultType} ${resultName} = ${default};";
        
        public string SourceCode => GenerateSource(Ins, CustomTemplateValues);
        
        protected virtual Dictionary<string, string> CreateTemplateMap()
        {
            return new Dictionary<string, string>();
        }
        
        protected virtual string GenerateDefaultSource()
        {
            return ShaderNodesUtil.Evaluate(DefaultShaderCode, CreateTemplateMap());
        }

        private string GenerateSource(IEnumerable<AbstractGpuValue> theIns, IDictionary<string, string> theCustomValues = null)
        {
            
            if (ShaderNodesUtil.HasNullValue(theIns))
            {
                return GenerateDefaultSource();
            }

            var sourceCode = SourceTemplate();
            if (sourceCode.Trim() == "") return "";

            var templateMap = CreateTemplateMap();
            theCustomValues?.ForEach(kv => templateMap.Add(kv.Key, kv.Value));

            return ShaderNodesUtil.Evaluate(sourceCode, templateMap);
        }

        public void BuildChildrenSource(StringBuilder theSourceBuilder, HashSet<int> theHashes)
        {
            Children.ForEach(child =>
            {
                if (!(child is AbstractShaderNode input)) return;
               
                input.BuildSource(theSourceBuilder, theHashes);
            });
        }

        protected internal virtual void BuildSource(StringBuilder theSourceBuilder, HashSet<int> theHashes)
        {
            BuildChildrenSource(theSourceBuilder, theHashes);

            var source = SourceCode;
            if (!string.IsNullOrWhiteSpace(source) && theHashes.Add(GetHashCode()))
            {
                theSourceBuilder.Append("        " + source + Environment.NewLine);
            }
        }
       
        public string BuildSourceCode()
        {
            var myStringBuilder = new StringBuilder();
            var myHashes = new HashSet<int>();

            BuildSource(myStringBuilder, myHashes);
            
            return myStringBuilder.ToString();
        }
        
        public List<IFunctionParameter> Delegates()
        {
            var result = new HashSet<IFunctionParameter>();
            Trees.ReadOnlyTreeNode.Flatten(this).ForEach(n =>
            {
                if(n is IFunctionParameter input)result.Add(input);
            });
            return result.ToList();
        }
        
        private delegate List<T> GetInfoElement<T>(AbstractShaderNode theInput);
        
        private  List<T> GetInfo<T>(GetInfoElement<T> theDelegate)
        {
            var result = new HashSet<T>();
            Trees.ReadOnlyTreeNode.Flatten(this).ForEach(n =>
            {
                if(n is AbstractShaderNode input)result.AddRange(theDelegate(input));
            });
            return result.ToList();
        }
        
        public List<TResourceType> ResourceForTree<TResourceType>(string theResourceId)
        {
            var result = new HashSet<TResourceType>();
            Trees.ReadOnlyTreeNode.Flatten(this).ForEach(n =>
            {
                if (!(n is AbstractShaderNode input) || !input.Resources.ContainsKey(theResourceId)) return;
                Stride.Core.Extensions.EnumerableExtensions.ForEach<TResourceType>(input.Resources[theResourceId], i => result.Add(i));

            });
            return result.ToList();
        }

        public List<string> ResourceIdsForTree()
        {
            var result = new HashSet<string>();
            Trees.ReadOnlyTreeNode.Flatten(this).ForEach(n =>
            {
                if (!(n is AbstractShaderNode input)) return;
                input.Resources.ForEach(kv =>
                {
                    if (result.Add(kv.Key)) return;
                });
            });
            return result.ToList();
        }
        
        public Dictionary<string, IList> ResourcesForTree()
        {
            var result = new Dictionary<string, IList>();
            Trees.ReadOnlyTreeNode.Flatten(this).ForEach(n =>
            {
                if (!(n is AbstractShaderNode input)) return;
                input.Resources.ForEach(kv =>
                {
                    if (result.ContainsKey(kv.Key)) return;
                    
                    var list = new ArrayList();
                    ResourceForTree<object>(kv.Key).ForEach(i => list.Add(i));
                    result[kv.Key] = list;
                });
            });
            return result;
        }
        
        public Dictionary<string, List<TResource>> ResourcesForTree<TResource>()
        {
            var result = new Dictionary<string, List<TResource>>();
            Trees.ReadOnlyTreeNode.Flatten(this).ForEach(n =>
            {
                if (!(n is AbstractShaderNode input)) return;
                input.Resources.ForEach(kv =>
                {
                    var values = kv.Value.OfType<TResource>();
                    if (values.IsEmpty()) return;
                    if (!result.ContainsKey(kv.Key))
                    {
                        result[kv.Key] = new List<TResource>();
                    }
                    values.ForEach(v => result[kv.Key].Add(v));
                });
            });
            return result;
        }

        public Dictionary<string, IList> Resources { get; } = new Dictionary<string, IList>();

        public void AddResource(string theResourceId, object theResource)
        {
            if (!Resources.ContainsKey(theResourceId))
            {
                Resources[theResourceId] = new ArrayList();
            }

            Resources[theResourceId].Add(theResource);
        }
        
        public void AddResources(string theResourceId, IList theResources)
        {
            if (!Resources.ContainsKey(theResourceId))
            {
                Resources[theResourceId] = new ArrayList();
            }

            Stride.Core.Extensions.EnumerableExtensions.ForEach<object>(theResources, r => Resources[theResourceId].Add(r));

        }

        protected const string Mixins = "Mixins";
        protected const string Inputs = "Inputs";
        protected const string Declarations = "Declarations";
        protected const string Structs = "Structs";

        public List<string> MixinList()
        {
            return ResourceForTree<string>(Mixins);
        }
        
        public List<IGpuInput> InputList()
        {
            return ResourceForTree<IGpuInput>(Inputs);
        }
        
        public List<FieldDeclaration> DeclarationList()
        {
            return ResourceForTree<FieldDeclaration>(Declarations);
        }
        
        public List<string> StructList()
        {
            return ResourceForTree<string>(Structs);
        }
        
        public Dictionary<string,string> FunctionMap(){
       
            var result = new Dictionary<string,string>();
            Trees.ReadOnlyTreeNode.Flatten(this).ForEach(n =>
            {
                if (!(n is AbstractShaderNode input)) return;
                
                input.Functions?.ForEach(kv =>
                {
                    if (result.ContainsKey(kv.Key)) return;
                    
                    result.Add(kv.Key, kv.Value);
                });
                
            });
            return result;
        }
    }
    
    public abstract class ShaderNode<T> : AbstractShaderNode
    {
        public  GpuValue<T> Output { get; protected set; }
        public  ConstantValue<T> Default { get; protected set; }

        protected ShaderNode(string theId, ConstantValue<T> theDefault = null,string outputName = "result") : base(theId)
        {
            Default = theDefault ?? ConstantHelper.FromFloat<T>(0);
            Output = new GpuValue<T>(outputName);
        }

        protected override Dictionary<string, string> CreateTemplateMap()
        {
            return new Dictionary<string, string>
            {
                {"resultName", Output.ID},
                {"resultType", Output != null ? Output.TypeName() : TypeHelpers.GetGpuTypeForType<T>()},
                {"default", Default == null ? "": Default.ID},
                {"arguments", ShaderNodesUtil.BuildArguments(Ins)}
            };
        }

        public override AbstractGpuValue AbstractOutput()
        {
            return Output;
        }

        protected void Setup(IEnumerable<AbstractGpuValue> theIns, IDictionary<string, string> theCustomValues = null)
        {
            CustomTemplateValues = theCustomValues;
            Ins = theIns.ToList();
            Output.ParentNode = this;
        }
    }
}