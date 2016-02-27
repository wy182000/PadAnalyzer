﻿using Dia2Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenerateUserTypesFromPdb.UserTypes
{
    class UserTypeFactory
    {
        protected List<UserType> userTypes = new List<UserType>();
        protected XmlTypeTransformation[] typeTransformations;

        public UserTypeFactory(XmlTypeTransformation[] transformations)
        {
            typeTransformations = transformations;
        }

        public UserTypeFactory(UserTypeFactory factory)
            : this(factory.typeTransformations)
        {
            //#fixme why!
            //
            userTypes.AddRange(factory.userTypes);
        }

        public List<UserType> Symbols
        {
            get
            {
                return userTypes;
            }
        }

        internal virtual bool TryGetUserType(string typeString, out UserType userType)
        {
            return GlobalCache.UserTypesBySymbolName.TryGetValue(typeString, out userType);
        }

        internal virtual bool GetUserType(IDiaSymbol type, out UserType userType)
        {
            //#fixme, remove primitive types
            userType = null;

            if ((SymTagEnum)type.symTag == SymTagEnum.SymTagBaseType)
            {
                switch ((BasicType)type.baseType)
                {
                    case BasicType.Bit:
                    case BasicType.Bool:
                        userType = new PrimitiveUserType("bool", type);
                        break;
                    case BasicType.Char:
                    case BasicType.WChar:
                        userType = new PrimitiveUserType("char", type);
                        break;
                    case BasicType.BSTR:
                        userType = new PrimitiveUserType("string", type);
                        break;
                    case BasicType.Void:
                        userType = new PrimitiveUserType("void", type);
                        break;
                    case BasicType.Float:
                        userType = new PrimitiveUserType(type.length <= 4 ? "float" : "double", type);
                        break;
                    case BasicType.Int:
                    case BasicType.Long:
                        switch (type.length)
                        {
                            case 0:
                                userType = new PrimitiveUserType("void", type);
                                break;
                            case 1:
                                userType = new PrimitiveUserType("sbyte", type);
                                break;
                            case 2:
                                userType = new PrimitiveUserType("short", type);
                                break;
                            case 4:
                                userType = new PrimitiveUserType("int", type);
                                break;
                            case 8:
                                userType = new PrimitiveUserType("long", type);
                                break;
                            default:
                                throw new Exception("Unexpected type length " + type.length);
                        }
                        break;
                    case BasicType.UInt:
                    case BasicType.ULong:
                        switch (type.length)
                        {
                            case 0:
                                userType = new PrimitiveUserType("void", type);
                                break;
                            case 1:
                                userType = new PrimitiveUserType("byte", type);
                                break;
                            case 2:
                                userType = new PrimitiveUserType("ushort", type);
                                break;
                            case 4:
                                userType = new PrimitiveUserType("uint", type);
                                break;
                            case 8:
                                userType = new PrimitiveUserType("ulong", type);
                                break;
                            default:
                                throw new Exception("Unexpected type length " + type.length);
                                
                        }
                        break;
                    default:
                        break;
                }
            }

            if ((SymTagEnum)type.symTag == SymTagEnum.SymTagPointerType)
            {
                IDiaSymbol pointerType = type.type;

                switch ((SymTagEnum)pointerType.symTag)
                {
                    case SymTagEnum.SymTagBaseType:
                    case SymTagEnum.SymTagEnum:
                        {
                            UserType innerType;
                            if (this.GetUserType(pointerType, out innerType))
                            {
                                if (innerType.ClassName == "void")
                                    userType = new PrimitiveUserType("NakedPointer", type);
                                if (innerType.ClassName == "char")
                                    userType = new PrimitiveUserType("string", type);
                            }
                            break;
                        }
                        /*
                            case SymTagEnum.SymTagUDT:
                                return GetTypeString(pointerType, factory);
                            default:
                                return new UserTypeTreeCodePointer(GetTypeString(pointerType, factory));
                        */
                }
            }

            // 

            if (userType != null)
            {
                return true;
            }

            string typeString = TypeToString.GetTypeString(type);

            // Try single lookup, this should match type directly
            typeString = NameHelper.GetSimpleLookupNameForSymbol(type);
            GlobalCache.UserTypesBySymbolName.TryGetValue(typeString, out userType);
            if (userType is PhysicalUserType || userType is EnumUserType)
            {
                return true;
            }

            if (userType != null)
            {
                throw new InvalidOperationException();
            }

            // Try generic lookup
            typeString = NameHelper.GetLookupNameForSymbol(type);
            GlobalCache.UserTypesBySymbolName.TryGetValue(typeString, out userType);

            if (userType == null)
            {
                return false;
            }

            // Return if physical type or EnumType
            if (userType is PhysicalUserType || userType is EnumUserType)
            {
                return true;
            }

            // For Template Type Find right specialization
            if (userType is TemplateUserType)
            {
                typeString = TypeToString.GetTypeString(type);

                TemplateUserType specializedUserType = ((TemplateUserType)userType).specializedTypes.FirstOrDefault(r => typeString == TypeToString.GetTypeString(r.Symbol));

                if (specializedUserType != null)
                {
                    //
                    //  TODO, just copy for now
                    //  Template type needs to know all other specializations.
                    //
                    specializedUserType.specializedTypes = ((TemplateUserType)userType).specializedTypes;
                    specializedUserType.NamespaceSymbol = userType.NamespaceSymbol;
                    specializedUserType.DeclaredInType = userType.DeclaredInType;

                    userType = specializedUserType;
                }
                else
                {
                    // We could not find the specialized template.
                    // Return null in this case.
                    // 
                    userType = null;
                }

                return userType != null;
            }

            return false;
        }

        internal void AddUserType(UserType userType)
        {
            if (userType.Symbol.name.Contains("_s_ThrowInfo"))
            {

            }

            //#fixme
            userTypes.Add(userType);
        }

        internal void InserUserType(UserType userType)
        {
            userTypes.Insert(0, userType);
        }

        internal void AddSymbol(IDiaSymbol symbol, XmlType type, string moduleName, UserTypeGenerationFlags generationOptions)
        {
            UserType newUserType;

            if (type == null)
            {
                newUserType = new EnumUserType(symbol, moduleName);
            }
            else if (generationOptions.HasFlag(UserTypeGenerationFlags.GeneratePhysicalMappingOfUserTypes))
            {
                newUserType = new PhysicalUserType(symbol, type, moduleName);
            }
            else
            {
                newUserType = new UserType(symbol, type, moduleName);
            }

            // Store in global cache
            string typeName = newUserType.Symbol.name;
            if (GlobalCache.UserTypesBySymbolName.TryAdd(typeName, newUserType))
            {
                userTypes.Add(newUserType);
            }
            else
            {

            }
        }

        internal void AddSymbols(IDiaSession session, IEnumerable<IDiaSymbol> symbols, XmlType type, string moduleName, UserTypeGenerationFlags generationOptions)
        {
            if (!type.IsTemplate && symbols.Any())
                throw new Exception("Type has more than one symbol for " + type.Name);

            if (!type.IsTemplate)
            {
                AddSymbol(symbols.First(), type, moduleName, generationOptions);
            }
            else
            {
                var buckets = new Dictionary<int, TemplateUserType>();

                foreach (IDiaSymbol diaSymbol in symbols)
                {
                    try
                    {
                        // We want to ignore "empty" generic classes (for now)
                        if (diaSymbol.name == null || diaSymbol.length == 0)
                        {
                            continue;
                        }

                        if (diaSymbol.name == "std::_Iosb<int>")
                        {

                        }

                        TemplateUserType templateType = new TemplateUserType(session, diaSymbol, type, moduleName, this);

                        int templateArgs = templateType.GenericsArguments;

                        if (templateArgs == 0)
                        {
                            // Template does not have arguments that can be used by generic 
                            // Make it specialized type
                            XmlType xmlType = new XmlType()
                            {
                                Name = diaSymbol.name
                            };

                            this.AddSymbol(diaSymbol, xmlType, moduleName, generationOptions);
                            continue;
                        }

                        TemplateUserType previousTemplateType;

                        if (!buckets.TryGetValue(templateArgs, out previousTemplateType))
                        {
                            // Add new template type
                            buckets.Add(templateArgs, templateType);
                            templateType.specializedTypes.Add(templateType);
                        }
                        else
                        {
                            previousTemplateType.specializedTypes.Add(templateType);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                // Add newly generated types
                foreach (var template in buckets.Values)
                {
                    userTypes.Add(template);

                    string symbolName = NameHelper.GetLookupNameForSymbol(template.Symbol);

                    if (!GlobalCache.UserTypesBySymbolName.TryAdd(symbolName, template))
                    {
                        throw new Exception();
                    }
                }
            }
        }

        /// <summary>
        /// Process Types
        ///     Set Namespace or Parent Type
        /// </summary>
        internal void ProcessTypes()
        {
            int index = 0;

            foreach (UserType userType in userTypes)
            {
                string symbolName = userType.Symbol.name;

                if (symbolName.Contains("CControlFlowGraph::ComputeEnidNodeGraph::__l2::Visitor"))
                {

                }

                List<string> namespaces = NameHelper.GetFullSymbolNamespaces(symbolName);

                if (namespaces.Count() == 1)
                {
                    // Class is not defined in namespace nor in type.
                    continue;
                }

                string symbolNamespace = string.Empty;
                string searchNamespace = string.Empty;

                symbolNamespace += namespaces[0];
                searchNamespace += NameHelper.GetLookupNameForSymbol(namespaces[0]);
                int parentSymbolNamespaceIndex = 0;

                UserType parentUserType = null;

                // Scan namespaces looking for parent
                for (int i = 1; i <= namespaces.Count() - 1; i++)
                {
                    UserType parentUserTypeLookup;

                    //
                    //  TODO
                    //  Verify, choose specialized type first
                    //  Then lookup template parent.
                    //

                    // First look up parent by name
                    GlobalCache.UserTypesBySymbolName.TryGetValue(symbolNamespace, out parentUserTypeLookup);

                    if (parentUserTypeLookup == null)
                    {
                        // Try to look up generic parent
                        GlobalCache.UserTypesBySymbolName.TryGetValue(searchNamespace, out parentUserTypeLookup);
                    }

                    if (parentUserTypeLookup != null)
                    {
                        parentUserType = parentUserTypeLookup;
                        parentSymbolNamespaceIndex = symbolNamespace.Length + ((i != namespaces.Count() - 1) ? 2 : 0);
                    }

                    if (i != namespaces.Count() - 1)
                    {
                        symbolNamespace += "::" + namespaces[i];
                        searchNamespace += "::" + NameHelper.GetLookupNameForSymbol(namespaces[i]); ;
                    }
                }

                // We found the parent type, but continue the search
                userType.SetDeclaredInType(parentUserType);

                // Remove Parent Namespace
                if (parentSymbolNamespaceIndex > 0 )
                {
                    symbolNamespace = symbolNamespace.Substring(parentSymbolNamespaceIndex);
                }

                userType.NamespaceSymbol = symbolNamespace;

                // we done 

                Console.WriteLine("{0}:{1}", index++, userTypes.Count());
            }
        }

        internal bool ContainsSymbol(IDiaSymbol type)
        {
            UserType userType;

            string typeString = TypeToString.GetTypeString(type);

            GlobalCache.UserTypesBySymbolName.TryGetValue(typeString, out userType);

            return (userType != null);
        }

        internal UserTypeTransformation FindTransformation(IDiaSymbol type, UserType ownerUserType)
        {
            string originalFieldTypeString = TypeToString.GetTypeString(type);
            var transformation = typeTransformations.Where(t => t.Matches(originalFieldTypeString)).FirstOrDefault();

            if (transformation == null)
                return null;

            Func<string, string> typeConverter = null;

            typeConverter = (inputType) =>
            {
                UserType userType;

                if (TryGetUserType(inputType, out userType))
                {
                    return userType.FullClassName;
                }

                var tr = typeTransformations.Where(t => t.Matches(inputType)).FirstOrDefault();

                if (tr != null)
                {
                    return tr.TransformType(inputType, ownerUserType.ClassName, typeConverter);
                }

                return "Variable";
            };

            return new UserTypeTransformation(transformation, typeConverter, ownerUserType, type);
        }

        internal bool ContainsSymbol(string typeString)
        {
            UserType userType;

            return TryGetUserType(typeString, out userType);
        }
    }
}
