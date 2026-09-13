from pathlib import Path
import re, sys
root=Path(sys.argv[1])
H=root/'cs2/velocity-cs2/project/core/systems/systems.hpp'
S=root/'cs2/velocity-cs2/project/core/systems/impl/schemas.cpp'
P=root/'cs2/velocity-cs2/project/core/systems/impl/prediction.cpp'

def req(c,m):
    if not c: raise SystemExit(m)

def funspan(t,sig):
    a=t.find(sig); req(a>=0,'missing '+sig); b=t.find('{',a); d=0
    for i in range(b,len(t)):
        d += (t[i]=='{')-(t[i]=='}')
        if d==0:return a,i+1,b
    raise SystemExit('unbalanced '+sig)

def patch_header():
    t=H.read_text()
    if '#include <optional>' not in t:t=t.replace('#include <filesystem>','#include <filesystem>\n#include <optional>',1)
    old='[[nodiscard]] static std::uint32_t lookup( const char* class_name, std::uint32_t field_hash );'
    if 'try_lookup(' not in t:
        req(old in t,'lookup declaration missing')
        t=t.replace(old,'[[nodiscard]] static std::optional<std::uint32_t> try_lookup( const char* class_name, std::uint32_t field_hash );\n\t\t'+old,1)
    H.write_text(t)

def patch_schema():
    t=S.read_text(); sig='std::uint32_t schemas::lookup( const char* class_name, std::uint32_t field_hash )'
    if 'schemas::try_lookup' in t:return
    a,e,_=funspan(t,sig); f=t[a:e].replace(sig,'std::optional<std::uint32_t> schemas::try_lookup( const char* class_name, std::uint32_t field_hash )',1)
    f=re.sub(r'(?m)^(\s*)return 0;\s*$',r'\1return std::nullopt;',f)
    wrap='\n\n\tstd::uint32_t schemas::lookup( const char* class_name, std::uint32_t field_hash )\n\t{\n\t\treturn try_lookup( class_name, field_hash ).value_or( 0 );\n\t}'
    S.write_text(t[:a]+f+wrap+t[e:])

def inject(t,sig,ret,phase):
    a,e,b=funspan(t,sig); f=t[a:e]
    if f'missing schema %s::%s ({phase})' in f:return t,0
    pairs=[]
    for x in re.findall(r'SCHEMA\(\s*"([^"]+)"\s*,\s*"([^"]+)"_hash\s*\)',f):
        if x not in pairs:pairs.append(x)
    req(pairs,'no schema fields in '+phase)
    rows='\n'.join(f'\t\t\t{{ "{c}", "{n}"_hash, "{n}" }},' for c,n in pairs)
    g=f'''\n\t\tstruct crashfix_schema {{ const char* c; std::uint32_t h; const char* n; }};\n\t\tstatic constexpr crashfix_schema crashfix_required[] = {{\n{rows}\n\t\t}};\n\t\tfor ( const auto& x : crashfix_required )\n\t\t{{\n\t\t\tif ( !systems::schemas::try_lookup( x.c, x.h ) )\n\t\t\t{{\n\t\t\t\tstatic std::atomic<bool> logged{{}};\n\t\t\t\tif ( !logged.exchange( true ) ) logging::console::print( "[prediction] disabled: missing schema %s::%s ({phase})", x.c, x.n );\n\t\t\t\t{ret}\n\t\t\t}}\n\t\t}}\n'''
    r=b+1-a; nf=f[:r]+g+f[r:]
    return t[:a]+nf+t[e:],len(pairs)

def patch_prediction():
    t=P.read_text()
    t,n1=inject(t,'void prediction::capture_prestate( std::uintptr_t local_pawn, std::uintptr_t movement_services )','return;','capture_prestate')
    t,n2=inject(t,'bool prediction::simulate( input::usercmd* cmd, const systems::local::snapshot& local, const std::function<void( )>& fn )','return false;','simulate')
    P.write_text(t); print('schema guards',n1,n2)

patch_header();patch_schema();patch_prediction()
