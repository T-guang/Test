using ElectricalSim.Rules;
using System.Linq;

namespace ElectricalSim.Practice
{
    public class PracticeScore
    {
        public int TotalScore { get; set; }
        public string Grade { get; set; }
    }

    /// <summary>
    /// 根据旧规则检查结果和旧连接检查结果计算当前兼容评分及等级。
    /// 当前公式会对致命规则问题、连接分数、Error 与 Warning 扣分，并在最低分边界处归零；本类不重新检查接线、
    /// 不生成教学反馈，也不决定练习会话生命周期。修改任一权重、阈值或 Clamp 前必须回归正确、部分错误、严重错误和零分边界。
    /// </summary>
    public static class PracticeScoreCalculator
    {
        // 这是保留旧结果模型的评分入口；当前 Netlist 主提交流程不应通过修改这里间接改变结构化连接判定。
        public static PracticeScore Calculate(CircuitCheckResult ruleResult, ConnectionCheckResult connectionResult)
        {
            if (ruleResult == null || connectionResult == null)
            {
                return new PracticeScore { TotalScore = 0, Grade = "未知状态" };
            }

            // Fatal issues checking
            bool hasFatalError = ruleResult.issues.Any(e => 
                (e.severity == CircuitIssueSeverity.Error) && 
                (e.message.Contains("未检测到有效电源") || 
                 e.message.Contains("未检测到有效负载") || 
                 e.message.Contains("未检测到任何导线")));

            if (hasFatalError || connectionResult.MatchScore == 0)
            {
                return new PracticeScore { TotalScore = 0, Grade = "未通过" };
            }

            int score = connectionResult.MatchScore;
            score -= ruleResult.ErrorCount * 15;
            score -= ruleResult.WarningCount * 5;

            if (score < 0) score = 0;

            string grade;
            if (score >= 90) grade = "通过";
            else if (score >= 75) grade = "基本通过";
            else if (score >= 60) grade = "需要修改";
            else grade = "未通过";

            return new PracticeScore { TotalScore = score, Grade = grade };
        }
    }
}
