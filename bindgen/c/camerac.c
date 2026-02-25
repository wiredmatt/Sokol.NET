/* Suppress visibility attribute so clang AST only emits ParmVarDecl nodes
 * inside FunctionDecl - Attr nodes would cause gen_ir.py to drop the func. */
#ifdef CAMERAC_API
#undef CAMERAC_API
#endif
#define CAMERAC_API
#include "ext/camerac/include/camerac.h"