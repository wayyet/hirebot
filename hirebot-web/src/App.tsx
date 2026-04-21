import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import Layout from './components/Layout'
import MarketPage from './pages/MarketPage'
import TemplateDetailPage from './pages/TemplateDetailPage'
import HiringPage from './pages/HiringPage'
import TeamPage from './pages/TeamPage'
import InstanceDetailPage from './pages/InstanceDetailPage'
import EvaluationPage from './pages/EvaluationPage'
import CollaborationPage from './pages/CollaborationPage'
import CollaborationGroupDetailPage from './pages/CollaborationGroupDetailPage'
import CustomEmployeePage from './pages/CustomEmployeePage'
import TrainingFlowPage from './pages/TrainingFlowPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Navigate to="/market" replace />} />
        <Route path="/market" element={<Layout><MarketPage /></Layout>} />
        <Route path="/custom-employee" element={<Layout><CustomEmployeePage /></Layout>} />
        <Route path="/custom-employee/:conversationId" element={<Layout><CustomEmployeePage /></Layout>} />
        <Route path="/templates/:id" element={<Layout><TemplateDetailPage /></Layout>} />
<Route path="/hiring/:templateId" element={<Layout><HiringPage /></Layout>} />
        <Route path="/hiring" element={<Layout><HiringPage /></Layout>} />
        <Route path="/team" element={<Layout><TeamPage /></Layout>} />
        <Route path="/instances/:id" element={<Layout><InstanceDetailPage /></Layout>} />
        <Route path="/instances/:id/evaluation" element={<Layout><EvaluationPage /></Layout>} />
        <Route path="/instances/:id/training" element={<Layout><TrainingFlowPage /></Layout>} />
        <Route path="/collaboration" element={<Layout><CollaborationPage /></Layout>} />
        <Route path="/collaboration/:groupId" element={<Layout><CollaborationGroupDetailPage /></Layout>} />
      </Routes>
    </BrowserRouter>
  )
}
