(function(){var r=document.documentElement,t=document.getElementById('theme'),p=document.getElementById('print');
function s(){var d=r.getAttribute('data-theme')==='dark';t.setAttribute('aria-pressed',d?'true':'false');t.lastChild.textContent=d?'Tema claro':'Tema escuro';}
t.addEventListener('click',function(){r.setAttribute('data-theme',r.getAttribute('data-theme')==='dark'?'light':'dark');s();});
p.addEventListener('click',function(){window.print();});s();})();
