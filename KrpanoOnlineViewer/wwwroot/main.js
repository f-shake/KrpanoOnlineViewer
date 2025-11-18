// DOM元素
const uploadArea = document.getElementById('uploadArea');
const fileInput = document.getElementById('fileInput');
const progressContainer = document.getElementById('progressContainer');
const progressFill = document.getElementById('progressFill');
const progressText = document.getElementById('progressText');
const statusMessage = document.getElementById('statusMessage');
const panoramaList = document.getElementById('panoramaList');

// 当前处理的ID
let currentProcessingId = null;
let statusCheckInterval = null;

// 事件监听器
uploadArea.addEventListener('click', () => fileInput.click());
uploadArea.addEventListener('dragover', handleDragOver);
uploadArea.addEventListener('dragleave', handleDragLeave);
uploadArea.addEventListener('drop', handleDrop);
fileInput.addEventListener('change', handleFileSelect);


// --- Cookie 工具函数 ---
function setCookie(name, value, days) {
    const expires = new Date(Date.now() + days * 24 * 60 * 60 * 1000).toUTCString();
    document.cookie = `${name}=${encodeURIComponent(value)}; expires=${expires}; path=/`;
}

function getCookie(name) {
    const match = document.cookie.match(new RegExp('(^| )' + name + '=([^;]+)'));
    return match ? decodeURIComponent(match[2]) : null;
}

// --- 密码处理 ---
let accessPassword = getCookie('accessPassword');

// 加载全景图列表
loadPanoramaList();

function handleDragOver(e) {
    e.preventDefault();
    uploadArea.classList.add('highlight');
}

function handleDragLeave(e) {
    e.preventDefault();
    uploadArea.classList.remove('highlight');
}

function handleDrop(e) {
    e.preventDefault();
    uploadArea.classList.remove('highlight');

    const files = e.dataTransfer.files;
    if (files.length > 0) {
        processFile(files[0]);
    }
}

function handleFileSelect(e) {
    e.preventDefault();
    const file = e.target.files[0];
    if (file) {
        processFile(file);
    }
}

async function processFile(file) {
    // 验证文件类型
    const allowedTypes = ['image/jpeg', 'image/png', 'image/tiff'];
    if (!allowedTypes.includes(file.type)) {
        showStatus('请选择 JPG、PNG 或 TIFF 格式的图片文件', 'error');
        return;
    }

    // 验证文件大小 (500MB)
    if (file.size > 500 * 1024 * 1024) {
        showStatus('文件大小不能超过 500MB', 'error');
        return;
    }

    const formData = new FormData();
    formData.append('file', file);

    try {
        showProgress('正在上传', 0);

        const response = await fetchWithPassword('api/upload', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            throw new Error('上传失败');
        }

        const result = await response.json();
        currentProcessingId = result.id;

        showProgress('开始处理', 30);
        startStatusPolling();

    } catch (error) {
        showStatus('上传失败: ' + error.message, 'error');
        hideProgress();
    }
}

function startStatusPolling() {
    if (statusCheckInterval) {
        clearInterval(statusCheckInterval);
    }

    statusCheckInterval = setInterval(async () => {
        if (!currentProcessingId) return;

        try {
            const response = await fetchWithPassword(`api/status/${currentProcessingId}`);
            if (!response.ok) {
                throw new Error('状态检查失败');
            }

            const status = await response.json();
            console.log(status)
            switch (status.status) {
                case 'completed':
                    showProgress('处理完成!', 100);
                    showStatus('全景图转换完成!', 'success');
                    clearInterval(statusCheckInterval);

                    setTimeout(() => {
                        // 方法1：使用相对路径（推荐）
                        const newURL = `panoramas/${currentProcessingId}/tour.html`;
                        console.log('Opening URL:', newURL);
                        window.open(newURL, '_blank');

                        // 或者方法2：构建绝对路径
                        // const baseURL = window.location.origin;
                        // const newURL = `${baseURL}/panoramas/${currentProcessingId}/tour.html`;
                        // window.open(newURL, '_blank');
                    }, 1000);

                    loadPanoramaList();

                    break;
                case 'error':
                    showStatus('处理失败: ' + status.error, 'error');
                    clearInterval(statusCheckInterval);
                    hideProgress();
                    break;
                default:
                    showProgress(status.status, status.progress);
            }

        } catch (error) {
            console.error('状态检查错误:', error);
        }
    }, 2000);
}

function showProgress(text, percent) {
    progressContainer.style.display = 'block';
    progressFill.style.width = percent + '%';
    progressText.textContent = text;
}

function hideProgress() {
    progressContainer.style.display = 'none';
    progressFill.style.width = '0%';
}

function showStatus(message, type) {
    statusMessage.textContent = message;
    statusMessage.className = 'status-message status-' + type;
    statusMessage.style.display = 'block';

    if (type === 'success' || type === 'error') {
        setTimeout(() => {
            statusMessage.style.display = 'none';
        }, 5000);
    }
}

async function loadPanoramaList() {
    try {
        const response = await fetchWithPassword('api/panoramas');

        if (!response.ok) {
            const text = await response.text(); // 读取原始文本（可能不是 JSON）
            throw new Error(text || `HTTP 错误 ${response.status}`);
        }

        const panoramas = await response.json();

        if (panoramas.length === 0) {
            panoramaList.innerHTML = '<div style="text-align: center; padding: 30px; color: #7f8c8d; font-size: 0.9em;">暂无全景图</div>';
            return;
        }

        panoramaList.innerHTML = panoramas.map(panorama => `
                <div class="panorama-item">
                    <div class="panorama-info">
                        <h3>${escapeHtml(panorama.name)}</h3>
                        <div class="panorama-meta">
                            ${new Date(panorama.createdAt).toLocaleDateString('zh-CN')} ${new Date(panorama.createdAt).toLocaleTimeString('zh-CN')}
                        </div>
                    </div>
                    <div class="panorama-actions">
                        <a href="panoramas/${panorama.id}/tour.html" class="view-btn" target="_blank">查看</a>
                        <button class="edit-btn" onclick="editPanorama('${panorama.id}', '${escapeHtml(panorama.name)}')">修改</button>
                        <button class="delete-btn" onclick="deletePanorama('${panorama.id}', '${escapeHtml(panorama.name)}')">删除</button>
                    </div>
                </div>
            `).join('');

    } catch (error) {
        console.log(error)
        panoramaList.innerHTML = `<div style="text-align: center; padding: 30px; color: #e74c3c; font-size: 0.9em;">加载失败：${error.message}</div>`;
        console.error('加载全景图列表失败:', error);
    }
}

function formatFileSize(bytes) {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function escapeHtml(unsafe) {
    return unsafe
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

function showPasswordModal() {
    document.getElementById('passwordModal').style.display = 'flex';
    document.getElementById('passwordInput').value = accessPassword || '';
    document.getElementById('passwordInput').focus();
}

document.getElementById('passwordSubmit').addEventListener('click', async () => {
    const input = document.getElementById('passwordInput').value.trim();
    if (!input) {
        alert('请输入密码');
        return;
    }

    try {
        // 使用临时密码进行测试
        const testResponse = await fetch('api/panoramas', {
            headers: {
                'X-Access-Token': input
            }
        });

        if (!testResponse.ok) {
            // 如果密码错误或未授权
            const text = await testResponse.text();
            alert('密码错误，请重新输入：' + text);
            return;
        }

        // 密码正确
        accessPassword = input;
        setCookie('accessPassword', accessPassword, 365); // 保存1年
        document.getElementById('passwordModal').style.display = 'none';

        // 重新加载全景图列表
        loadPanoramaList();

    } catch (error) {
        alert('验证密码时出错：' + error.message);
        console.error(error);
    }
});


// 点击修改按钮弹出密码输入
document.getElementById('changePasswordBtn').addEventListener('click', showPasswordModal);

// 如果没有密码，启动时弹出
if (!accessPassword) {
    showPasswordModal();
}

// --- fetch 封装 ---
// 替换原来的 fetch 调用，加上 X-Access-Token
async function fetchWithPassword(url, options = {}) {
    options.headers = options.headers || {};
    if (accessPassword) {
        options.headers['X-Access-Token'] = accessPassword;
    }
    return fetch(url, options);
}

// 修改全景图名称
async function editPanorama(id, currentName) {
    const newName = prompt('请输入新的全景图名称：', currentName);
    if (newName && newName.trim() !== '' && newName !== currentName) {
        try {
            const response = await fetchWithPassword(`api/panoramas/${id}`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({name: newName.trim()})
            });

            if (response.ok) {
                showStatus('全景图名称修改成功', 'success');
                loadPanoramaList(); // 重新加载列表
            } else {
                throw new Error('修改失败');
            }
        } catch (error) {
            showStatus('修改失败: ' + error.message, 'error');
        }
    }
}

// 删除全景图
async function deletePanorama(id, name) {
    if (confirm(`确定要删除全景图 "${name}" 吗？此操作不可撤销。`)) {
        try {
            const response = await fetchWithPassword(`api/panoramas/${id}`, {
                method: 'DELETE'
            });

            if (response.ok) {
                showStatus('全景图删除成功', 'success');
                loadPanoramaList(); // 重新加载列表
            } else {
                throw new Error('删除失败');
            }
        } catch (error) {
            showStatus('删除失败: ' + error.message, 'error');
        }
    }
}