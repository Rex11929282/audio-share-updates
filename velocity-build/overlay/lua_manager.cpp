#include <pch/pch.hpp>
#include <algorithm>
#include <array>
#include <chrono>
#include <fstream>
#include <iterator>
#include <lua.hpp>
#include <utilities/logging/logging.hpp>
#include "scripting.hpp"

namespace {
constexpr std::size_t max_script=1024u*1024u;
constexpr int hook_budget=250000;
void budget(lua_State* L,lua_Debug*){luaL_error(L,"instruction budget exceeded");}
std::string name(lua_State* L){lua_getfield(L,LUA_REGISTRYINDEX,"velocity.name");const char* s=lua_tostring(L,-1);std::string r=s?s:"?";lua_pop(L,1);return r;}
int logfn(lua_State* L){size_t n{};const char* s=luaL_checklstring(L,1,&n);auto x="[lua:"+name(L)+"] "+std::string(s,n);logging::console::print_raw(x.c_str());return 0;}
int nowfn(lua_State* L){using namespace std::chrono;lua_pushinteger(L,(lua_Integer)duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count());return 1;}
void safe_libs(lua_State* L){struct X{const char*n;lua_CFunction f;};constexpr X x[]={{"_G",luaopen_base},{LUA_TABLIBNAME,luaopen_table},{LUA_STRLIBNAME,luaopen_string},{LUA_MATHLIBNAME,luaopen_math},{LUA_UTF8LIBNAME,luaopen_utf8}};for(auto&a:x){luaL_requiref(L,a.n,a.f,1);lua_pop(L,1);}lua_pushnil(L);lua_setglobal(L,"dofile");lua_pushnil(L);lua_setglobal(L,"loadfile");}
void api(lua_State* L,const std::filesystem::path&p){auto n=p.filename().string();lua_pushlstring(L,n.data(),n.size());lua_setfield(L,LUA_REGISTRYINDEX,"velocity.name");lua_newtable(L);lua_pushcfunction(L,logfn);lua_setfield(L,-2,"log");lua_pushcfunction(L,nowfn);lua_setfield(L,-2,"now_ms");lua_pushliteral(L,"1.0");lua_setfield(L,-2,"api_version");lua_setglobal(L,"velocity");}
bool readf(const std::filesystem::path&p,std::string&o){std::error_code e;auto z=std::filesystem::file_size(p,e);if(e||z>max_script)return false;std::ifstream f(p,std::ios::binary);if(!f)return false;o.assign(std::istreambuf_iterator<char>(f),{});return true;}
}
namespace scripting {
bool lua_manager::initialize(void* h){std::scoped_lock l(m_mutex);if(m_initialized)return true;std::array<wchar_t,32768>b{};auto n=GetModuleFileNameW((HMODULE)h,b.data(),(DWORD)b.size());if(!n||n>=b.size())return false;m_script_directory=std::filesystem::path(b.data()).parent_path()/L"scripts";std::error_code e;std::filesystem::create_directories(m_script_directory,e);if(e)return false;m_last_frame=std::chrono::steady_clock::now();m_initialized=true;scan_unlocked(false);logging::console::print("[lua] initialized; scripts={}",m_script_directory.string());return true;}
void lua_manager::shutdown(){std::scoped_lock l(m_mutex);for(auto&x:m_scripts)unload_script_unlocked(*x);m_scripts.clear();m_initialized=false;}
void lua_manager::reload_all(){std::scoped_lock l(m_mutex);if(m_initialized)scan_unlocked(true);}
void lua_manager::on_frame(){std::scoped_lock l(m_mutex);if(!m_initialized)return;auto n=std::chrono::steady_clock::now();if(m_last_scan.time_since_epoch().count()==0||n-m_last_scan>=std::chrono::seconds(1)){scan_unlocked(false);m_last_scan=n;}double dt=std::chrono::duration<double>(n-m_last_frame).count();m_last_frame=n;for(auto&x:m_scripts)if(x->state)call_unlocked(*x,"on_frame",dt,true);}
void lua_manager::scan_unlocked(bool force){std::error_code e;std::vector<std::filesystem::path> f;for(auto&x:std::filesystem::directory_iterator(m_script_directory,e))if(x.is_regular_file(e)&&x.path().extension()==L".lua")f.push_back(x.path());std::sort(f.begin(),f.end());for(auto i=m_scripts.begin();i!=m_scripts.end();)if(std::find(f.begin(),f.end(),(*i)->path)==f.end()){unload_script_unlocked(**i);i=m_scripts.erase(i);}else ++i;for(auto&p:f){auto wt=std::filesystem::last_write_time(p,e);if(e){e.clear();continue;}auto i=std::find_if(m_scripts.begin(),m_scripts.end(),[&](auto&x){return x->path==p;});if(i==m_scripts.end()){auto x=std::make_unique<script>();x->path=p;x->write_time=wt;load_script_unlocked(*x);m_scripts.push_back(std::move(x));}else if(force||(*i)->write_time!=wt){unload_script_unlocked(**i);(*i)->write_time=wt;load_script_unlocked(**i);}}}
void lua_manager::load_script_unlocked(script&x){std::string s;if(!readf(x.path,s)){log_error_unlocked(x,"load","read failed/too large");return;}x.state=luaL_newstate();if(!x.state)return;safe_libs(x.state);api(x.state,x.path);lua_sethook(x.state,budget,LUA_MASKCOUNT,hook_budget);int r=luaL_loadbufferx(x.state,s.data(),s.size(),x.path.filename().string().c_str(),"t");if(r==LUA_OK)r=lua_pcall(x.state,0,0,0);lua_sethook(x.state,nullptr,0,0);if(r!=LUA_OK){auto e=lua_tostring(x.state,-1);log_error_unlocked(x,"load",e?e:"error");lua_close(x.state);x.state=nullptr;return;}logging::console::print("[lua] loaded {}",x.path.filename().string());call_unlocked(x,"on_load",0,false);}
void lua_manager::unload_script_unlocked(script&x){if(!x.state)return;call_unlocked(x,"on_unload",0,false);lua_close(x.state);x.state=nullptr;}
bool lua_manager::call_unlocked(script&x,const char*c,double v,bool arg){lua_getglobal(x.state,c);if(!lua_isfunction(x.state,-1)){lua_pop(x.state,1);return true;}if(arg)lua_pushnumber(x.state,v);lua_sethook(x.state,budget,LUA_MASKCOUNT,hook_budget);int r=lua_pcall(x.state,arg?1:0,0,0);lua_sethook(x.state,nullptr,0,0);if(r!=LUA_OK){auto e=lua_tostring(x.state,-1);log_error_unlocked(x,c,e?e:"error");lua_pop(x.state,1);return false;}return true;}
void lua_manager::log_error_unlocked(const script&x,const char*p,const char*e)const{logging::console::print("[lua] {} {}: {}",x.path.filename().string(),p?p:"?",e?e:"?");}
}
