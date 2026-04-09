import { dotnet } from './_framework/dotnet.js'

const { setModuleImports, runMain } = await dotnet.create();

const sidebar = document.getElementById('sidebar');
const output = document.getElementById('output');
const outputTitle = document.getElementById('output-title');

const clickQueue = [];
const pendingClicks = [];

setModuleImports('main.js', {
    addSidebarItem(index, name, desc) {
        const btn = document.createElement('button');
        btn.className = 'example-btn';
        btn.dataset.index = index;
        btn.innerHTML = `<span class="name">${name}</span><span class="desc">${desc}</span>`;
        btn.addEventListener('click', () => {
            if (clickQueue.length > 0) {
                clickQueue.shift()(index);
            } else {
                pendingClicks.push(index);
            }
        });
        sidebar.appendChild(btn);
    },
    setOutput(title, text) {
        outputTitle.textContent = title;
        output.className = '';
        output.textContent = text;
    },
    setOutputLoading(title) {
        outputTitle.textContent = title;
        output.className = '';
        output.textContent = 'Running\u2026';
    },
    setActive(index) {
        sidebar.querySelectorAll('.example-btn').forEach(b =>
            b.classList.toggle('active', Number(b.dataset.index) === index)
        );
    },
    waitForClick() {
        if (pendingClicks.length > 0) {
            return Promise.resolve(pendingClicks.shift());
        }
        return new Promise(resolve => clickQueue.push(resolve));
    },
});

await runMain();
