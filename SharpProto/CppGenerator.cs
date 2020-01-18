﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SharpProto
{
    public class CppGenerator : GeneratorBase, IGenerator
    {
        protected readonly Dictionary<string, string> primitiveTypeMapping = new Dictionary<string, string>()
        {
            {"String", "std::string" },
            {"Int32", "int" },
            {"Int64", "long long" },
            {"Single", "float" },
            {"Double", "double" },
        };

        protected readonly IList<string> nonReferenceTypes = new List<string>()
        {
            "Int32",
            "Int64",
            "Single",
            "Double"
        };

        public CppGenerator()
        {
        }

        public bool Generate(string Filename, string OutputDir)
        {
            var assembly = base.CompileProto(Filename);

            var types = base.ValidateProtos(assembly);

            var stem = Path.GetFileNameWithoutExtension(Filename);

            var hFilename = Path.Join(OutputDir, stem + ".pb.h");            

            File.WriteAllText(hFilename, GenerateHeader(stem, types));
            return true;
        }

        private string GenerateHeader(string stem, Type[] types)
        {
            var guard = "_" + stem.ToUpper() + "_";

            var template =
@"// DO NOT MODIFY! CODE GENERATED BY SHARPPROTO.
#ifndef {0}
#define {0}

#include <string>

{1}

#endif // {0}";

            var sb = new StringBuilder();
            sb.AppendFormat("namespace {0} {{\n", types[0].Namespace);

            foreach (Type t in types)
            {
                sb.Append(GenerateClassDef(t));
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.AppendFormat("}}  // namespace {0}\n", types[0].Namespace);

            return string.Format(template, guard, sb.ToString());
        }

        private string GetTypeName(string name)
        {
            return primitiveTypeMapping.ContainsKey(name) ? primitiveTypeMapping[name] : name;            
        }

        private string GenerateClassDef(Type t)
        {
            var sb = new StringBuilder();

            sb.AppendFormat("class {0} {{\n", t.Name);
            
            var sbPublic = new StringBuilder();
            var sbPrivate = new StringBuilder();
           
            
            foreach (var fieldInfo in t.GetFields())
            {
                var fieldAttr = fieldInfo.GetCustomAttribute<FieldAttribute>();                

                var fieldType = fieldInfo.FieldType;

                if (fieldType.IsGenericType)
                {
                    if (fieldType.GetGenericTypeDefinition() == typeof(IList<>))
                    {
                        var argType = fieldType.GetGenericArguments()[0];
                        throw new NotSupportedException("repeated fields not supported yet");
                    }
                } else {                
                    // primitive types, e.g int
                    var reference = nonReferenceTypes.Contains(fieldType.Name) ? "" : "&";
                    var typeName = GetTypeName(fieldType.Name);
                    sbPublic.AppendFormat("  const {0}{2} {1}() const {{ return {1}_; }}\n", typeName, fieldInfo.Name, reference);
                    sbPublic.AppendFormat("  void set_{1}(const {0}{2} val) {{ {1}_ = val; }}\n", typeName, fieldInfo.Name, reference);
                    sbPrivate.AppendFormat("  {0} {1}_; // = {2}\n", typeName, fieldInfo.Name, fieldAttr.ID);
                }                  
            }

            sb.AppendFormat(" public:\n");

            sb.Append(sbPublic);

            sb.AppendFormat(" private:\n");

            sb.Append(sbPrivate);

            sb.AppendFormat("}};\n");

            return sb.ToString();
        }        
    }
}
